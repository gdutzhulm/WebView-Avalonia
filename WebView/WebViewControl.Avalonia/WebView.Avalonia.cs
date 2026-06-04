using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WebViewControl {

    partial class WebView : BaseControl {

        private static bool _imeFixApplied = false;
        private static int _imeFixAttempts = 0;
        private const int MaxImeFixAttempts = 20;

        [DllImport("libFixIME.dylib", EntryPoint = "apply_avnview_ime_fix")]
        private static extern int ApplyAvnViewImeFix();

        private static void ApplyImeFixIfNeeded() {
            if (_imeFixApplied) return;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
            if (_imeFixAttempts >= MaxImeFixAttempts) return;

            try {
                _imeFixAttempts++;
                var result = ApplyAvnViewImeFix();
                if (result == 1) {
                    _imeFixApplied = true;
                    return;
                }

                Debug.WriteLine($"IME fix attempt {_imeFixAttempts} failed: AvnView not ready.");
            } catch (Exception ex) {
                Debug.WriteLine($"IME fix attempt {_imeFixAttempts} failed: {ex.Message}");
            }

            ScheduleImeFixRetry();
        }

        private static void ScheduleImeFixRetry() {
            _ = Task.Delay(100).ContinueWith(
                _ => Dispatcher.UIThread.Post(ApplyImeFixIfNeeded, DispatcherPriority.Background),
                TaskScheduler.Default);
        }

        private bool IsInDesignMode => false;

        public static readonly StyledProperty<string> AddressProperty =
            AvaloniaProperty.Register<WebView, string>(nameof(Address), defaultBindingMode: BindingMode.TwoWay);

        public string Address {
            get => GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);

            if (change.Property == AddressProperty) {
                InternalAddress = Address;
            }
        }

        partial void ExtraInitialize() {
            VisualChildren.Add(chromium);
            chromium[!FocusableProperty] = this[!FocusableProperty];
            chromium.AddressChanged += (o, address) => ExecuteInUI(() => Address = address);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            ApplyImeFixIfNeeded();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (AllowDeveloperTools && e.Key == Key.F12) {
                ToggleDeveloperTools();
                e.Handled = true;
            }
        }

        protected override void OnGotFocus(GotFocusEventArgs e) {
            if (!e.Handled) {
                e.Handled = true;
                base.OnGotFocus(e);

                // use async call to avoid reentrancy, otherwise the webview will fight to get the focus
                Dispatcher.UIThread.Post(() => {
                    if (IsFocused) {
                        chromium.Focus();
                    }
                }, DispatcherPriority.Background);
            }
        }

        protected override void InternalDispose() => Dispose();

        private void ForwardException(ExceptionDispatchInfo exceptionInfo) {
            // TODO
        }

        private T ExecuteInUI<T>(Func<T> action) {
            if (Dispatcher.UIThread.CheckAccess()) {
                return action();
            }
            return Dispatcher.UIThread.InvokeAsync<T>(action).Result;
        }

        private void AsyncExecuteInUI(Action action) {
            if (isDisposing) {
                return;
            }
            // use async call to avoid dead-locks, otherwise if the source action tries to to evaluate js it would block
            Dispatcher.UIThread.InvokeAsync(
                () => {
                    if (!isDisposing) {
                        ExecuteWithAsyncErrorHandling(action);
                    }
                },
                DispatcherPriority.Normal);
        }

        internal void InitializeBrowser(int initialWidth, int initialHeight) {
            ApplyImeFixIfNeeded();
            chromium.CreateBrowser(initialWidth, initialHeight);
        }

        /// <summary>
        /// Called when the webview is requesting focus. Return false to allow the
        /// focus to be set or true to cancel setting the focus.
        /// <paramref name="isSystemEvent">True if is a system focus event, or false if is a navigation</paramref>
        /// </summary>
        protected virtual bool OnSetFocus(bool isSystemEvent) {
            // VisualRoot can be null when webview is not yet added to the Visual tree
            var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement();
            return !(focusedElement == chromium || focusedElement == this);
        }
    }
}
