// 맥용 앱 묶음(.app)을 만들어 tar.gz로 포장한다. 저장소 루트에서 실행:
//   dotnet run tools/package-mac.cs
// Windows에서 zip으로 묶으면 실행 권한이 사라져 맥에서 열리지 않으므로,
// tar 항목마다 유닉스 권한(0755/0644)을 직접 지정한다.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;

const string AppName = "AutoClickerMac";
const string Rid = "osx-arm64";
const string Version = "0.1.0";

string root = Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, AppName, $"{AppName}.csproj")))
{
    Console.Error.WriteLine("저장소 루트(AutoClickerMac 폴더가 있는 곳)에서 실행하세요.");
    return 1;
}

string outDir = Path.Combine(root, "publish", "mac");
string publishDir = Path.Combine(outDir, "_publish");
string appDir = Path.Combine(outDir, $"{AppName}.app");
string archivePath = Path.Combine(root, "publish", $"{AppName}-arm64.tar.gz");

// 1. publish
if (Directory.Exists(outDir))
    Directory.Delete(outDir, recursive: true);
var psi = new ProcessStartInfo("dotnet", $"publish {AppName} -c Release -r {Rid} --self-contained -o \"{publishDir}\" -nologo -v q")
{
    WorkingDirectory = root,
};
using (var proc = Process.Start(psi)!)
{
    proc.WaitForExit();
    if (proc.ExitCode != 0)
    {
        Console.Error.WriteLine("dotnet publish 실패");
        return proc.ExitCode;
    }
}

// 2. .app 조립
string macOsDir = Path.Combine(appDir, "Contents", "MacOS");
Directory.CreateDirectory(macOsDir);
Directory.CreateDirectory(Path.Combine(appDir, "Contents", "Resources"));
foreach (string file in Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories))
{
    if (file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
        continue;
    string dest = Path.Combine(macOsDir, Path.GetRelativePath(publishDir, file));
    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
    File.Copy(file, dest);
}
Directory.Delete(publishDir, recursive: true);

File.WriteAllText(Path.Combine(appDir, "Contents", "Info.plist"), $"""
    <?xml version="1.0" encoding="UTF-8"?>
    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
    <plist version="1.0">
    <dict>
        <key>CFBundleName</key><string>{AppName}</string>
        <key>CFBundleDisplayName</key><string>{AppName}</string>
        <key>CFBundleIdentifier</key><string>com.reuscap.autoclickermac</string>
        <key>CFBundleExecutable</key><string>{AppName}</string>
        <key>CFBundlePackageType</key><string>APPL</string>
        <key>CFBundleVersion</key><string>{Version}</string>
        <key>CFBundleShortVersionString</key><string>{Version}</string>
        <key>LSMinimumSystemVersion</key><string>12.0</string>
        <key>NSHighResolutionCapable</key><true/>
        <key>NSPrincipalClass</key><string>NSApplication</string>
    </dict>
    </plist>
    """.Replace("\r\n", "\n"));

// 3. tar.gz (권한 직접 지정)
const UnixFileMode Exec = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
const UnixFileMode Normal = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

int execCount = 0;
using (var fs = File.Create(archivePath))
using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
using (var tar = new TarWriter(gz, TarEntryFormat.Pax))
{
    string baseDir = Path.GetDirectoryName(appDir)!;
    foreach (string dir in Directory.EnumerateDirectories(appDir, "*", SearchOption.AllDirectories).Prepend(appDir).Order())
    {
        string name = Path.GetRelativePath(baseDir, dir).Replace('\\', '/') + "/";
        tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name) { Mode = Exec, ModificationTime = DateTimeOffset.UtcNow });
    }
    foreach (string file in Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories).Order())
    {
        bool isExec = IsMachO(file);
        if (isExec) execCount++;
        using var data = File.OpenRead(file);
        tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, Path.GetRelativePath(baseDir, file).Replace('\\', '/'))
        {
            Mode = isExec ? Exec : Normal,
            ModificationTime = DateTimeOffset.UtcNow,
            DataStream = data,
        });
    }
}

// 4. 결과와 코드 서명 여부
string mainBinary = Path.Combine(macOsDir, AppName);
Console.WriteLine($"앱 묶음: {appDir}");
Console.WriteLine($"압축 파일: {archivePath} ({new FileInfo(archivePath).Length / 1024.0 / 1024.0:0.0} MB)");
Console.WriteLine($"실행 권한(0755)을 준 Mach-O 파일: {execCount}개");
Console.WriteLine(HasCodeSignature(mainBinary)
    ? "메인 실행 파일: ad-hoc 코드 서명 있음"
    : "메인 실행 파일: 코드 서명 없음 → 맥에서 'codesign --force --deep -s - AutoClickerMac.app' 필요");
return 0;

// Mach-O 실행 파일/라이브러리인지 매직 넘버로 판별 (64비트, 유니버설)
static bool IsMachO(string path)
{
    Span<byte> head = stackalloc byte[4];
    using var f = File.OpenRead(path);
    if (f.Read(head) < 4)
        return false;
    uint le = BinaryPrimitives.ReadUInt32LittleEndian(head);
    uint be = BinaryPrimitives.ReadUInt32BigEndian(head);
    return le == 0xFEEDFACF || be == 0xCAFEBABE;
}

// 64비트 Mach-O의 로드 커맨드 중 LC_CODE_SIGNATURE(0x1D)가 있는지 확인
static bool HasCodeSignature(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    if (bytes.Length < 32 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0xFEEDFACF)
        return false;
    uint ncmds = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16));
    int offset = 32;
    for (uint i = 0; i < ncmds && offset + 8 <= bytes.Length; i++)
    {
        uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
        if (cmd == 0x1D)
            return true;
        if (size == 0)
            break;
        offset += (int)size;
    }
    return false;
}
