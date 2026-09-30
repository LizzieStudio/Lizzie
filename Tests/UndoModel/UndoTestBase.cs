using System;
using CsCheck;

/// <summary>What every family of undo tests shares: the implementation under test.</summary>
public abstract class UndoTestBase
{
    protected abstract IUndoApi Actual { get; }

    /// <summary>Checks the property on generated sessions.</summary>
    protected static void Sample(Gen<Op[]> sessions, Action<Op[]> property) =>
        Sample(sessions, property, UndoSessions.Print);

    /// <summary>Checks the property on generated input.</summary>
    protected static void Sample<T>(Gen<T> input, Action<T> property, Func<T, string> print) =>
        input.Sample(property, print: print);
}
