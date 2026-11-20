using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class Disc
{
    internal const string Digest = "2318536199430db26cedc32f7019b75b42abcd4af221521e7e85149a2c7fa684";
    const int Raw = 2352, Payload = 2048;
    // INDEX 01 values from the supported pressing. Its PREGAP is synthetic.
    static readonly int[] AudioStarts = { 165067, 192673, 220129, 248618, 276074 };
    static readonly HashSet<string> Roots = new HashSet<string>(
        "bikes common language Manual misc Overlays RIDERS Sounds tracks UiData".Split(' '),
        StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> Files = new HashSet<string>(new[] { "Gameinst.ini", "IFORCE2.dll",
        "menudata.ini", "RedlineRacerREADME.TXT", "rlr_3dfx.exe", "rlr_D3d.exe", "type.ini" },
        StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> DirectPlay = new HashSet<string>(
        "dplayx.dll dpmodemx.dll dpwsockx.dll".Split(' '), StringComparer.OrdinalIgnoreCase);

    readonly Stream image;
    readonly string output;
    readonly byte[] sectors = new byte[Raw * 64];
    internal Disc(Stream image, string output) { this.image = image; this.output = output; }

    internal static string ResolveImage(string input)
    {
        string path = Path.GetFullPath(input);
        if (!String.Equals(Path.GetExtension(path), ".cue", StringComparison.OrdinalIgnoreCase)) return path;
        var lines = File.ReadLines(path).Where(s => s.TrimStart().StartsWith("FILE ", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (lines.Length != 1) throw new InvalidDataException("単一のBINまたはIMGを使うCUEに対応しています。");
        Match match = Regex.Match(lines[0], "^\\s*FILE\\s+\"([^\"]+)\"\\s+BINARY\\s*$", RegexOptions.IgnoreCase);
        if (!match.Success || Path.GetFileName(match.Groups[1].Value) != match.Groups[1].Value)
            throw new InvalidDataException("CUEと同じフォルダのBINまたはIMGを直接選択してください。");
        return Path.Combine(Path.GetDirectoryName(path), match.Groups[1].Value);
    }

    static void ReadFully(Stream input, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count) {
            int read = input.Read(buffer, offset, count - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    void CopyData(int lba, int length, Stream destination)
    {
        image.Position = (long)lba * Raw;
        while (length > 0) {
            int count = Math.Min(64, (length + Payload - 1) / Payload);
            ReadFully(image, sectors, count * Raw);
            for (int i = 0; i < count; i++) {
                int n = Math.Min(length, Payload);
                destination.Write(sectors, i * Raw + 16, n);
                length -= n;
            }
        }
    }

    byte[] ReadData(int lba, int length)
    {
        using (var data = new MemoryStream(length)) {
            CopyData(lba, length, data);
            return data.ToArray();
        }
    }

    void ExtractDirectory(int lba, int length, string relative)
    {
        byte[] data = ReadData(lba, length);
        for (int pos = 0; pos < length;) {
            int record = pos, size = data[pos];
            if (size == 0) { pos = (pos / Payload + 1) * Payload; continue; }
            pos += size;
            int chars = data[record + 32];
            if (chars == 1) continue; // Joliet '.' and '..'
            string name = Encoding.BigEndianUnicode.GetString(data, record + 33, chars);
            if (name.EndsWith(";1", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 2);
            if (name == "." || name == ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("ディスク上のファイル名が無効です。");
            bool directory = (data[record + 25] & 2) != 0;
            string path = Path.Combine(relative, name);
            if (relative.Length == 0 && !(directory ? Roots.Contains(name) || name == "directx" : Files.Contains(name))) continue;
            if (relative == "directx") {
                if (directory || !DirectPlay.Contains(name)) continue;
                path = name;
            }
            int sector = BitConverter.ToInt32(data, record + 2);
            int bytes = BitConverter.ToInt32(data, record + 10);
            if (directory) {
                if (path != "directx") Directory.CreateDirectory(Path.Combine(output, path));
                ExtractDirectory(sector, bytes, path);
            } else {
                using (var file = new FileStream(Path.Combine(output, path), FileMode.CreateNew, FileAccess.Write))
                    CopyData(sector, bytes, file);
            }
        }
    }

    internal void ExtractFiles()
    {
        byte[] descriptor = ReadData(17, Payload);
        if (descriptor[0] != 2 || Encoding.ASCII.GetString(descriptor, 1, 5) != "CD001")
            throw new InvalidDataException("Jolietディレクトリが見つかりません。");
        ExtractDirectory(BitConverter.ToInt32(descriptor, 158), BitConverter.ToInt32(descriptor, 166), "");
    }

    internal void ExtractMusic()
    {
        string music = Directory.CreateDirectory(Path.Combine(output, "Music")).FullName;
        for (int i = 0; i < AudioStarts.Length; i++) {
            long end = i + 1 < AudioStarts.Length ? (long)AudioStarts[i + 1] * Raw : image.Length;
            image.Position = (long)AudioStarts[i] * Raw;
            int length = checked((int)(end - image.Position));
            using (var wave = new BinaryWriter(File.Create(Path.Combine(music, String.Format("Track{0:D2}.wav", i + 2))))) {
                wave.Write(Encoding.ASCII.GetBytes("RIFF")); wave.Write(36 + length);
                wave.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); wave.Write(16);
                wave.Write((short)1); wave.Write((short)2); wave.Write(44100); wave.Write(176400);
                wave.Write((short)4); wave.Write((short)16); wave.Write(Encoding.ASCII.GetBytes("data")); wave.Write(length);
                while (length > 0) {
                    int count = Math.Min(sectors.Length, length);
                    ReadFully(image, sectors, count);
                    wave.Write(sectors, 0, count);
                    length -= count;
                }
            }
        }
    }
}
