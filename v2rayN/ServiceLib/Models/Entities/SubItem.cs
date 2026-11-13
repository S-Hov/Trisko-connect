namespace ServiceLib.Models.Entities;

[Serializable]
public class SubItem
{
    [PrimaryKey]
    public string Id { get; set; }

    public string Remarks { get; set; }

    public string Url { get; set; }

    public string MoreUrl { get; set; }

    public bool Enabled { get; set; } = true;

    public string UserAgent { get; set; } = string.Empty;

    public int Sort { get; set; }

    public string? Filter { get; set; }

    public int AutoUpdateInterval { get; set; }

    public long UpdateTime { get; set; }

    public string? ConvertTarget { get; set; }

    public string? PrevProfile { get; set; }

    public string? NextProfile { get; set; }

    public int? PreSocksPort { get; set; }

    public string? Memo { get; set; }

    public ECoreType? CustomCoreType { get; set; }


    [Column(Trisko.TriskoDataCompatibility.TrafficUploadColumn)]
    public long TriskoTrafficUpload { get; set; }

    [Column(Trisko.TriskoDataCompatibility.TrafficDownloadColumn)]
    public long TriskoTrafficDownload { get; set; }

    [Column(Trisko.TriskoDataCompatibility.TrafficTotalColumn)]
    public long TriskoTrafficTotal { get; set; }

    [Column(Trisko.TriskoDataCompatibility.TrafficExpireColumn)]
    public long TriskoTrafficExpire { get; set; }

    [Column(Trisko.TriskoDataCompatibility.TrafficUpdatedColumn)]
    public long TriskoTrafficUpdatedAt { get; set; }
}
