// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Front.Services;

public sealed class UndoRedoStack<T>(int capacity = 100) where T : class
{
    private readonly Stack<T> _undoStack = new();
    private readonly Stack<T> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Push(T state)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (_undoStack.Count >= capacity)
        {
            var retained = _undoStack.Take(capacity - 1).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var item in retained) _undoStack.Push(item);
        }
        _undoStack.Push(state);
        _redoStack.Clear();
    }

    public T? Undo(T current)
    {
        if (!CanUndo) return null;
        _redoStack.Push(current);
        return _undoStack.Pop();
    }

    public T? Redo(T current)
    {
        if (!CanRedo) return null;
        _undoStack.Push(current);
        return _redoStack.Pop();
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
