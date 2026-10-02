// SPDX-License-Identifier: EUPL-1.2

namespace Aetheus.Front.Tests;

public class UndoRedoStackTests
{
    [Fact]
    public void NewStack_CannotUndo()
    {
        var stack = new UndoRedoStack<string>();
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void NewStack_CannotRedo()
    {
        var stack = new UndoRedoStack<string>();
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Push_EnablesUndo()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state1");
        Assert.True(stack.CanUndo);
    }

    [Fact]
    public void Push_ClearsRedoStack()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state1");
        stack.Push("state2");
        stack.Undo("current");
        Assert.True(stack.CanRedo);
        stack.Push("state3");
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void Undo_ReturnsLastPushedState()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state1");
        stack.Push("state2");

        var result = stack.Undo("current");
        Assert.Equal("state2", result);

        result = stack.Undo("state2");
        Assert.Equal("state1", result);
    }

    [Fact]
    public void Undo_EmptyStack_ReturnsNull()
    {
        var stack = new UndoRedoStack<string>();
        Assert.Null(stack.Undo("current"));
    }

    [Fact]
    public void Undo_EnablesRedo()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state1");
        stack.Undo("current");
        Assert.True(stack.CanRedo);
    }

    [Fact]
    public void Redo_ReturnsUndoneState()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("state1");
        stack.Undo("current");

        var result = stack.Redo("state1");
        Assert.Equal("current", result);
    }

    [Fact]
    public void Redo_EmptyStack_ReturnsNull()
    {
        var stack = new UndoRedoStack<string>();
        Assert.Null(stack.Redo("current"));
    }

    [Fact]
    public void Redo_AfterPush_CannotRedo()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("s1");
        stack.Undo("cur");
        stack.Push("s2");
        Assert.Null(stack.Redo("s2"));
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("s1");
        stack.Push("s2");
        stack.Undo("cur");

        stack.Clear();

        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoRedo_FullCycle()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("A");
        stack.Push("B");
        stack.Push("C");

        var r1 = stack.Undo("D");
        Assert.Equal("C", r1);

        var r2 = stack.Undo("C");
        Assert.Equal("B", r2);

        var r3 = stack.Redo("B");
        Assert.Equal("C", r3);

        var r4 = stack.Redo("C");
        Assert.Equal("D", r4);

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void MultipleUndos_ThenPush_ClearsRedo()
    {
        var stack = new UndoRedoStack<string>();
        stack.Push("A");
        stack.Push("B");
        stack.Push("C");
        stack.Undo("D");
        stack.Undo("C");

        Assert.True(stack.CanRedo);

        stack.Push("X");
        Assert.False(stack.CanRedo);

        var undone = stack.Undo("Y");
        Assert.Equal("X", undone);
    }

    [Fact]
    public void Push_DropsOldestStateWhenCapacityIsReached()
    {
        var stack = new UndoRedoStack<string>(capacity: 3);
        stack.Push("A");
        stack.Push("B");
        stack.Push("C");
        stack.Push("D");

        Assert.Equal("D", stack.Undo("current"));
        Assert.Equal("C", stack.Undo("D"));
        Assert.Equal("B", stack.Undo("C"));
        Assert.Null(stack.Undo("B"));
    }
}
