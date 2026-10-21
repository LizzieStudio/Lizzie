using System;

namespace Lizzie;

/// <summary>
/// A project's save exists but can't be loaded. The message is written for the player.
/// </summary>
public class ProjectLoadException(string message) : Exception(message);
