/// <summary>
/// Something an event does to one atom.
/// 
/// <list type="bullet">
/// <item>writes its whole value, as a <see cref="Replicated"/></item>
/// <item>unsets it, as an <see cref="Unset"/></item>
/// </list>
/// </summary>
public interface IChange;

/// <summary>
/// Makes an atom act as if it was never written.
/// No Query returns it, and saves leave it out.
/// Made with <see cref="RecordReaderExtensions.Unset"/>.
/// Like a write, the newest in-effect one wins, so a later write can bring the atom back.
/// Within one event, an unset beats a write of the same atom.
/// </summary>
/// <param name="Atom">The atom to be deleted.</param>
public sealed record Unset(Replicated Atom) : IChange;
