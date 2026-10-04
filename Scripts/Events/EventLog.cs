using System.Collections.Generic;

namespace Lizzie.Replication.Machinery;

/// <summary>
/// Every event, by id, in id order. The source of truth that the records are worked out from.
/// </summary>
public sealed class EventLog : OrderedDictionary<SnowportId, TableEvent>;
