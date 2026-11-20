using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

internal static class RacerDisplay {
    static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string[] Modes = { "max", "max-stretch", "full", "full-stretch", "1", "1.5", "2", "3" };
    static readonly string[] Labels = { "最大化ウィンドウ（4:3）", "最大化ウィンドウ（ストレッチ）", "全画面（4:3）", "全画面（ストレッチ）", "1 倍　640 × 480", "1.5 倍　960 × 720", "2 倍　1280 × 960", "3 倍　1920 × 1440" };
    const int Style = -16, Caption = 0x00c00000, Resize = 0x00040000, MaxBox = 0x00010000;
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int command);

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left,top,right,bottom; }
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h,out Rect r);
    static void FitMaximized(IntPtr h) {
        Rect frame, client;
        if(!IsZoomed(h) || !GetWindowRect(h,out frame) || !GetClientRect(h,out client)) return;
        var work=Screen.FromHandle(h).WorkingArea;
        int extraW=frame.right-frame.left-client.right, extraH=frame.bottom-frame.top-client.bottom;
        int unit=Math.Min((work.Width-extraW)/4,(work.Height-extraH)/3);
        if(unit<1 || (client.right==4*unit && client.bottom==3*unit)) return;
        int width=unit*4+extraW,height=unit*3+extraH;
        SetWindowPos(h,IntPtr.Zero,work.Left+(work.Width-width)/2,work.Top+(work.Height-height)/2,width,height,0x14);
    }

    static string ReadMode() {
        string file = Path.Combine(Dir, "display-mode.txt");
        string mode = File.Exists(file) ? File.ReadAllText(file).Trim() : "max";
        return Array.IndexOf(Modes,mode) < 0 ? "max" : mode;
    }
    static string Setting(string text, string section, string key, string value) {
        Match block=Regex.Match(text, @"(?ms)^\[" + section + @"\][^\r\n]*\r?\n.*?(?=^\[|\z)");
        Regex entry=new Regex(@"(?m)^(" + key + @"[ \t]*=[ \t]*)[^\r\n]*(?=\r?$)");
        if(!block.Success || !entry.IsMatch(block.Value)) throw new InvalidDataException(section+" / "+key);
        string result=entry.Replace(block.Value, m => m.Groups[1].Value+value,1);
        return text.Remove(block.Index,block.Length).Insert(block.Index,result);
    }
    static bool Fullscreen(string mode) { return mode == "full" || mode == "full-stretch"; }
    static bool Maximized(string mode) { return mode == "max" || mode == "max-stretch"; }
    static Size WindowSize(string mode) {
        switch (mode) {
            case "1": return new Size(640,480);
            case "1.5": return new Size(960,720);
            case "2": return new Size(1280,960);
            case "3": return new Size(1920,1440);
            default: return Size.Empty;
        }
    }
    static void SizeWindow(IntPtr h, Size size) {
        Rect frame, client;
        if (!GetWindowRect(h,out frame) || !GetClientRect(h,out client)) return;
        int width=size.Width+frame.right-frame.left-client.right;
        int height=size.Height+frame.bottom-frame.top-client.bottom;
        var work=Screen.FromHandle(h).WorkingArea;
        SetWindowPos(h,IntPtr.Zero,work.Left+Math.Max(0,(work.Width-width)/2),
            work.Top+Math.Max(0,(work.Height-height)/2),width,height,0x14);
    }
    static void Configure(string mode) {
        var screen=Screen.PrimaryScreen;
        var area=Fullscreen(mode) ? screen.Bounds : screen.WorkingArea;
        Size size=WindowSize(mode);
        if(size.IsEmpty) {
            int unit=Math.Max(1,Math.Min(area.Width/4,area.Height/3));
            size=new Size(4*unit,3*unit);
        }
        string res=mode=="full-stretch" ? "desktop" : size.Width+"x"+size.Height;
        string path=Path.Combine(Dir,"dgVoodoo.conf");
        string old=File.ReadAllText(path,Encoding.Default), text=old;
        text=Setting(text,"General","FullScreenMode",Fullscreen(mode)?"true":"false");
        bool stretch=mode.EndsWith("-stretch",StringComparison.Ordinal);
        text=Setting(text,"General","ScalingMode",stretch?"stretched":"stretched_4_3");
        text=Setting(text,"General","KeepWindowAspectRatio",stretch?"false":"true");
        text=Setting(text,"GeneralExt","FullscreenAttributes","fake");
        text=Setting(text,"Glide","Resolution",res);
        if(text!=old) {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temp,text,Encoding.Default); File.Replace(temp,path,null);}
            finally {if(File.Exists(temp)) File.Delete(temp);}
        }
    }
    static void Run(string mode) {
        string exe=Path.Combine(Dir,"Redline Racer.exe");
        foreach(Process other in Process.GetProcessesByName("Redline Racer")) {
            using(other) {
                string path;
                try {path=other.MainModule.FileName;} catch {continue;}
                if(String.Equals(path,exe,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("ゲームはすでに起動しています。");
            }
        }
        Configure(mode);
        using(Process child=Process.Start(new ProcessStartInfo(exe){WorkingDirectory=Dir,UseShellExecute=false})) {
            bool maximize=Maximized(mode);
            Size initialSize=WindowSize(mode);
            while(!child.WaitForExit(250)) {
                child.Refresh();
                IntPtr h=child.MainWindowHandle;
                if(h==IntPtr.Zero || child.MainWindowTitle!="RedLine") continue;
                int style=GetWindowLong(h,Style);
                if((style&Caption)!=Caption) continue;
                if((style&(MaxBox|Resize))!=(MaxBox|Resize)) {
                    SetWindowLong(h,Style,style|MaxBox|Resize);
                    SetWindowPos(h,IntPtr.Zero,0,0,0,0,0x37);
                }
                if(maximize) { ShowWindowAsync(h,3); maximize=false; }
                if(!initialSize.IsEmpty) { SizeWindow(h,initialSize); initialSize=Size.Empty; }
                if(!mode.EndsWith("-stretch",StringComparison.Ordinal)) FitMaximized(h);
            }
        }
    }
    static void Choose() {
        using(Form form=new Form()) {
            form.Text="Redline Racer 表示設定";
            form.AutoScaleMode=AutoScaleMode.Dpi;
            form.Font=SystemFonts.MessageBoxFont;
            form.FormBorderStyle=FormBorderStyle.FixedDialog;
            form.MaximizeBox=false; form.MinimizeBox=false;
            form.StartPosition=FormStartPosition.CenterScreen;
            form.ClientSize=new Size(310,120);
            var list=new ComboBox {Left=16,Top=19,Width=278,DropDownStyle=ComboBoxStyle.DropDownList};
            list.Items.AddRange(Labels); list.SelectedIndex=Array.IndexOf(Modes,ReadMode());
            var save=new Button {Text="保存",Left=200,Top=72,Width=94,DialogResult=DialogResult.OK};
            form.Controls.Add(list);form.Controls.Add(save);form.AcceptButton=save;
            if(form.ShowDialog()==DialogResult.OK) {
                string mode=Modes[list.SelectedIndex];Configure(mode);
                File.WriteAllText(Path.Combine(Dir,"display-mode.txt"),mode,Encoding.ASCII);
            }
        }
    }
    [STAThread] static int Main(string[] args) {
        try {
            SetProcessDPIAware();
            if(args.Length==1 && args[0]=="--play") Run(ReadMode());
            else {Application.EnableVisualStyles(); Choose();}
            return 0;
        } catch(Exception e) {MessageBox.Show(e.Message,"Redline Racer",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
}
