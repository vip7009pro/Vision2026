using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisionInspectionApp.UI.Services;

public interface IUndoableAction
{
    void Do();
    void Undo();
}

public sealed class UndoRedoManager : ObservableObject
{
    private readonly Stack<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Execute(IUndoableAction action)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        action.Do();
        _undo.Push(action);
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var action = _undo.Pop();
        action.Undo();
        _redo.Push(action);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var action = _redo.Pop();
        action.Do();
        _undo.Push(action);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public sealed class DelegateAction(Action doAction, Action undoAction) : IUndoableAction
    {
        private readonly Action _do = doAction ?? throw new ArgumentNullException(nameof(doAction));
        private readonly Action _undo = undoAction ?? throw new ArgumentNullException(nameof(undoAction));

        public void Do() => _do();

        public void Undo() => _undo();
    }
}
