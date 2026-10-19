using AutoClicker.Native;
using AutoClicker.UI;

namespace AutoClicker;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 두 개가 동시에 켜지면 단축키를 서로 뺏으므로 하나만 실행한다.
        using var mutex = new Mutex(true, @"Local\AutoClicker.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("AutoClicker가 이미 실행 중입니다.", "AutoClicker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        NativeMethods.DisablePowerThrottling();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
