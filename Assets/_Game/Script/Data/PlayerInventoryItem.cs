using System;
using Postgrest.Attributes;
using Postgrest.Models;

// Model cho bảng 'skins'
[Table("skins")]
public class Skin : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("price")]
    public long Price { get; set; }

    [Column("asset_key")]
    public string AssetKey { get; set; }

    [Column("description")]
    public string Description { get; set; }

    // Trường này không có trong DB, dùng để xử lý logic UI
    public bool IsOwned { get; set; }
}

// Model cho bảng 'player_inventory'
//đây không phải cả cái inventory mà chỉ là 1 item trong inventory thôi, nên đáng ra tên là
//  PlayerInventoryItem cho dễ hiểu
[Table("player_inventory")]
public class PlayerInventoryItem : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("player_id")]
    public string PlayerId { get; set; }

    [Column("skin_id")]
    public long SkinId { get; set; }

    [Column("purchased_at")]
    public DateTime PurchasedAt { get; set; }
}