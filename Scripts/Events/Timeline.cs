using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Every event, by id, in id order. The source of truth that the atoms are worked out from.
/// </summary>
/// <remarks>
/// A project has one timeline. Its <see cref="Stage"/>s branch the project's state, not the timeline.
/// </remarks>
public sealed class Timeline : OrderedDictionary<SnowportId, TimelineEvent>;
