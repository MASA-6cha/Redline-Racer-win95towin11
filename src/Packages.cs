using System;
using System.IO;
using System.IO.Compression;
using System.Net;

internal static class Packages
{
    static void Install(string stage, string local, string url, string hash, params string[] entries)
    {
        string name = url.Substring(url.LastIndexOf('/') + 1);
        string offline = Path.Combine(local, name);
        bool download = !File.Exists(offline);
        string archive = download ? Path.Combine(stage, name) : offline;
        Console.WriteLine((download ? "公式配布元から取得中  " : "ローカルZIPを確認中  ") + name);
        if (download) {
            using (var client = new WebClient()) {
                client.Headers[HttpRequestHeader.UserAgent] = "Redline-Racer-Portable-Patch/1.0";
                client.DownloadFile(url, archive);
            }
        }
        using (var stream = File.OpenRead(archive)) {
            Converter.Verify(stream, hash, name);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read)) {
                for (int i = 0; i < entries.Length; i += 2) {
                    var entry = zip.GetEntry(entries[i]);
                    if (entry == null) throw new InvalidDataException(name + " に " + entries[i] + " がありません。");
                    using (var input = entry.Open())
                    using (var output = new FileStream(Path.Combine(stage, entries[i+1]), FileMode.CreateNew))
                        input.CopyTo(output);
                }
            }
        }
        if (download) File.Delete(archive);
    }

    internal static void InstallAll(string stage, string local)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        Install(stage, local, "https://github.com/dege-diosg/dgVoodoo2/releases/download/2.87.6/dgVoodoo2_87_6.zip",
            "9afa493396feec7108f42a2d97661eef96bbaac5addf0dfb5cfbc3001cd76a0a",
            "3Dfx/x86/Glide2x.dll", "Glide2x.dll", "dgVoodoo.conf", "dgVoodoo.conf");
        Install(stage, local, "https://github.com/Jione/Direct-WinMM/releases/download/v2.3.1/DirectWinMM-x86-v2.3.1.zip",
            "56a7a4a529b1b700dc1ce1483971ef1336798da5f2b48a4115938ff0b42b8813",
            "winmm.dll", "winmm.dll", "THIRD-PARTY-NOTICES.txt", "THIRD-PARTY-NOTICES.txt");
        const string real = "reAL-2.0.0/";
        Install(stage, local, "https://github.com/PlayNeth/reAL/releases/download/2.0.0/reAL-2.0.0.zip",
            "3167fe647766fac43b3dacf2a88f9d49227fb341599a695c8eac1e71b5b8ebde",
            real + "dsound.dll", "dsound.dll", real + "LICENSE", "reAL-LICENSE.txt",
            real + "LICENSE-HRTF", "reAL-HRTF-LICENSE.txt", real + "LICENSE-libmysofa", "reAL-libmysofa-LICENSE.txt");
    }
}
