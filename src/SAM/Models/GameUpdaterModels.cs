using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SAM.Models;

public class DepotInfo
{
    [JsonPropertyName("depotid")]
    public int DepotId { get; set; }

    [JsonPropertyName("gid")]
    public ulong Gid { get; set; }
}

public class SteamResponse
{
    [JsonPropertyName("depots")]
    public List<DepotInfo> Depots { get; set; } = new();
}
