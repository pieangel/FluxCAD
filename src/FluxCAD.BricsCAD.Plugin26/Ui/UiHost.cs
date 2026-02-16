using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FluxCAD.BricsCAD.Plugin26.Ui
{
    public static class UiHost
    {
        private static Thread? _uiThread;
        private static Dispatcher? _uiDispatcher;
        private static FluxCadWindowHost? _window;

        public static void Show()
        {
            // UI 스레드가 없으면 새로 만든다 (STA)
            if (_uiThread == null || !_uiThread.IsAlive || _uiDispatcher == null)
            {
                _uiThread = new Thread(UiThreadMain)
                {
                    IsBackground = true,
                    Name = "FluxCAD.UI",
                };
                _uiThread.SetApartmentState(ApartmentState.STA);
                _uiThread.Start();

                // Dispatcher가 준비될 때까지 잠깐 대기
                SpinWait.SpinUntil(() => _uiDispatcher != null, 3000);
            }

            // UI 스레드에서 창을 띄움/활성화
            _uiDispatcher?.Invoke(() =>
            {
                if (_window == null)
                {
                    EnsureWpfApp();

                    _window = new FluxCadWindowHost();
                    _window.Closed += (_, __) => _window = null;
                    _window.Show();
                }
                else
                {
                    if (!_window.IsVisible) _window.Show();
                    _window.WindowState = WindowState.Normal;
                    _window.Activate();
                    _window.Topmost = true;   // 포커스 보장
                    _window.Topmost = false;
                }
            });
        }

        private static void UiThreadMain()
        {
            _uiDispatcher = Dispatcher.CurrentDispatcher;
            Dispatcher.Run();
        }

        private static void EnsureWpfApp()
        {
            if (Application.Current == null)
            {
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }
        }
    }
}
