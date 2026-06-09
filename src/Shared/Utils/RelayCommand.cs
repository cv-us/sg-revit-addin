using System;
using System.Windows.Input;

namespace SgRevitAddin.Utils
{
    /// <summary>
    /// Minimal ICommand implementation for WPF MVVM. Wraps an Action and an
    /// optional CanExecute predicate. CanExecuteChanged is wired to
    /// CommandManager.RequerySuggested so the framework polls it whenever
    /// focus or input changes — good enough for the dialog-scale UIs we
    /// build here.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? (Predicate<object>)null : _ => canExecute())
        {
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
