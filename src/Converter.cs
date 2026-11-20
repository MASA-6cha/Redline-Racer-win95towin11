using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Converter
{
    static readonly string Home = AppDomain.CurrentDomain.BaseDirectory;
    internal static void Verify(Stream stream, string expected, string name)
    {
        using (var sha = SHA256.Create()) {
            string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (actual != expected) throw new InvalidDataException(name + " のSHA-256が対応版と一致しません。\n" + actual);
        }
        stream.Position = 0;
    }

    static void ReplaceSetting(string file, string old, string replacement)
    {
        string text = File.ReadAllText(file, Encoding.ASCII);
        if (text.IndexOf(old, StringComparison.Ordinal) < 0) throw new InvalidDataException(Path.GetFileName(file) + " に想定した設定がありません。");
        File.WriteAllText(file, text.Replace(old, replacement), Encoding.ASCII);
    }

    static void PatchByteRange(byte[] bytes, int offset, string hex)
    {
        for (int i = 0; i < hex.Length; i += 2) bytes[offset++] = Convert.ToByte(hex.Substring(i, 2), 16);
    }

    static void PatchGame(string folder)
    {
        string original = Path.Combine(folder, "rlr_3dfx.exe");
        byte[] bytes = File.ReadAllBytes(original);
        using (var input = new MemoryStream(bytes)) Verify(input,
            "84d3ee0cf75dbac351c3a7e95bdce713e5a6644b95d111a673a8ced4b9901770", "rlr_3dfx.exe");
        // Keep the selected rider geometry up to the existing visibility cutoff.
        PatchByteRange(bytes, 0x006fdd, "9a99993e");
        PatchByteRange(bytes, 0x006fed, "c50447eb6c9090");
        // Look for the installed game data in the working directory.
        PatchByteRange(bytes, 0x008510, "eb4c");
        PatchByteRange(bytes, 0x00855e, "6a006a009090909090");
        PatchByteRange(bytes, 0x008568, "22");
        PatchByteRange(bytes, 0x00856c, "e87f63080083c40c83f8ff740e50");
        PatchByteRange(bytes, 0x00857b, "c1e907");
        PatchByteRange(bytes, 0x00857f, "59b801000000c39031c0c3");
        // Menu confirmation on button 1 after a neutral interval.
        PatchByteRange(bytes, 0x01e3ec, "f646");
        PatchByteRange(bytes, 0x01e3ef, "01");
        PatchByteRange(bytes, 0x01e3fa, "7d");
        PatchByteRange(bytes, 0x01e403, "74");
        using (var result = new MemoryStream(bytes)) Verify(result,
            "1cecaffe165f82705f3b9a4ee3ad3ab5e086c9381b7667d31cdb31b55d798629", "Redline Racer.exe");
        File.WriteAllBytes(Path.Combine(folder, "Redline Racer.exe"), bytes);

        string ini = Path.Combine(folder, "menudata.ini");
        ReplaceSetting(ini, "FirstRun=True", "FirstRun=False");
        ReplaceSetting(ini, "GraphicsGeometryDetail=1", "GraphicsGeometryDetail=2");
        ReplaceSetting(ini, "GraphicsViewDistance=50", "GraphicsViewDistance=100");
        ReplaceSetting(Path.Combine(folder, "Gameinst.ini"), @"CD=c:\redline\redline\", @"CD=.\");
        string conf = Path.Combine(folder, "dgVoodoo.conf");
        ReplaceSetting(conf, "CaptureMouse                         = true", "CaptureMouse                         = false");
        ReplaceSetting(conf, "CenterAppWindow                      = false", "CenterAppWindow                      = true");
        ReplaceSetting(conf, "FPSLimit                             = 0", "FPSLimit                             = 60");
        ReplaceSetting(conf, "3DfxWatermark                       = true", "3DfxWatermark                       = false");
        // The launcher calculates the output resolution for the current monitor.
        ReplaceSetting(conf, "FullScreenMode                       = true", "FullScreenMode                       = false");
        foreach (string name in new[] { "Display Settings.exe", "display-mode.txt", "Play Redline Racer.cmd", "Redline Racer.exe.manifest" })
            File.Copy(Path.Combine(Home, "portable", name), Path.Combine(folder, name));
    }

    static void Build(string imagePath, string destination, string packages)
    {
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("出力先はすでに存在します。新しいフォルダ名を指定してください。\n" + destination);
        using (var source = File.OpenRead(imagePath)) {
            Console.WriteLine("ディスクイメージを確認中…");
            if (source.Length != 674939328) throw new InvalidDataException("対応版は音声を含む674,939,328バイトのBIN/IMGです。データのみのISOからは5曲のBGMを復元できません。");
            Verify(source, Disc.Digest, Path.GetFileName(imagePath));
            string parent = Directory.CreateDirectory(Path.GetDirectoryName(destination)).FullName;
            string stage = Path.Combine(parent, ".rlr-build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try {
                Packages.InstallAll(stage, packages);
                var disc = new Disc(source, stage);
                Console.WriteLine("ゲームデータを展開中…"); disc.ExtractFiles();
                Console.WriteLine("5曲のCD音源をWAVに変換中…"); disc.ExtractMusic();
                PatchGame(stage);
                // Windows may still be scanning a newly written executable.
                for (int attempt = 0; ; attempt++) {
                    try { Directory.Move(stage, destination); break; }
                    catch (UnauthorizedAccessException) { if (attempt >= 15) throw; }
                    catch (IOException) { if (attempt >= 15 || Directory.Exists(destination) || File.Exists(destination)) throw; }
                    Thread.Sleep(250);
                }
            } finally {
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
            }
        }
    }

    [STAThread] static int Main(string[] args)
    {
        bool picker = args.Length == 0;
        try {
            if (args.Length > 3 || (args.Length > 0 && args[0] == "--help")) {
                Console.WriteLine("Convert.exe [<disc.img|disc.bin|disc.cue> [<new output folder> [<package ZIP folder>]]]");
                return args.Length > 3 ? 2 : 0;
            }
            string input;
            if (picker) {
                Application.EnableVisualStyles();
                using (var dialog = new OpenFileDialog()) {
                    dialog.Title = "Redline Racer のディスクイメージを選択";
                    dialog.Filter = "CD イメージ (*.img;*.bin;*.cue)|*.img;*.bin;*.cue";
                    if (dialog.ShowDialog() != DialogResult.OK) return 0;
                    input = dialog.FileName;
                }
            } else input = args[0];
            string image = Disc.ResolveImage(input);
            string dest = args.Length >= 2 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetDirectoryName(image), "Redline Racer Win11");
            string packages = args.Length >= 3 ? Path.GetFullPath(args[2]) : Path.Combine(Home, "packages");
            Build(image, dest, packages);
            string message = "変換が完了しました。\n" + Path.Combine(dest, "Play Redline Racer.cmd");
            Console.WriteLine(message);
            if (picker) MessageBox.Show(message, "Redline Racer");
            return 0;
        } catch (Exception e) {
            Console.Error.WriteLine(picker ? e.Message : e.ToString());
            if (picker) MessageBox.Show(e.Message, "Redline Racer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
