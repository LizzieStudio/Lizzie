/// <summary>
/// One record's value before and after a change.
/// When old is null, it was added.
/// When new is null, it was removed.
/// </summary>
public readonly record struct RecordChange(object Old, object New);
