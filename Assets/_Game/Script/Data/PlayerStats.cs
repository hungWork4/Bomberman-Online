using Postgrest.Attributes;
using Postgrest.Models;

[Table("player_stats")]
public class PlayerStats : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; }

    [Column("gold")]
    public long Gold { get; set; }

    [Column("current_skin_id")]
    public long CurrentSkinId { get; set; }
}
