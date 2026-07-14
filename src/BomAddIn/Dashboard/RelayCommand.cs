using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace BomAddIn.Dashboard
{
    /// <summary>通用 RelayCommand — ICommand 的轻量实现，实现 IDisposable 以退订静态事件</summary>
    public class RelayCommand : ICommand, IDisposable
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        private readonly List<EventHandler> _handlers = new();

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add
            {
                if (value != null)
                {
                    _handlers.Add(value);
                    CommandManager.RequerySuggested += value;
                }
            }
            remove
            {
                if (value != null)
                {
                    _handlers.Remove(value);
                    CommandManager.RequerySuggested -= value;
                }
            }
        }

        public void Dispose()
        {
            foreach (var handler in _handlers)
                CommandManager.RequerySuggested -= handler;
            _handlers.Clear();
        }

        /// <summary>
        /// 手动触发 CanExecuteChanged，供 ViewModel 在状态变化时调用。
        /// 独立 WPF 线程中 CommandManager.RequerySuggested 可能不自动触发。
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
