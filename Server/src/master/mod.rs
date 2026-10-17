pub mod cache;
#[rustfmt::skip] // master-data-pipeline生成物。rustfmtが並び順を書き換えるのを防ぐ(Server/rustfmt.toml参照)
pub mod generated;

pub mod scout_banner;

pub use cache::MasterData;
pub use generated::{
    Items, MoveGroupMoves, MoveGroups, Moves, Pachimon, PachimonType, Rarity, ScoutBannerRates,
    ScoutBanners, ShopProducts, StarterPartySlots,
};
pub use scout_banner::{ScoutBanner, ScoutBannerRate};

/// master-data-pipelineが生成した各種マスタのJSON。
///
/// ビルド時にバイナリへ埋め込む(実行時のファイル読み込みやカレントディレクトリに
/// 依存させないため)。これらのJSON自体もmaster-data-pipelineの生成物であり、直接編集しない。
///
/// APIサーバーが起動時に参照するマスタデータの実体はMySQL(`cache::MasterData::load`)
/// であり、これらのJSONは`seed_master_data`コマンドがDBへ投入する際のシードデータとして
/// 利用する。
const PACHIMON_JSON: &str = include_str!("../../master_data/pachimon.json");
const MOVE_GROUPS_JSON: &str = include_str!("../../master_data/move_groups.json");
const MOVES_JSON: &str = include_str!("../../master_data/moves.json");
const MOVE_GROUP_MOVES_JSON: &str = include_str!("../../master_data/move_group_moves.json");
const STARTER_PARTY_SLOTS_JSON: &str = include_str!("../../master_data/starter_party_slots.json");
const ITEMS_JSON: &str = include_str!("../../master_data/items.json");
const SCOUT_BANNERS_JSON: &str = include_str!("../../master_data/scout_banners.json");
const SCOUT_BANNER_RATES_JSON: &str = include_str!("../../master_data/scout_banner_rates.json");
const SHOP_PRODUCTS_JSON: &str = include_str!("../../master_data/shop_products.json");

/// パチモンマスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_pachimon(json: &str) -> Result<Vec<Pachimon>, serde_json::Error> {
    serde_json::from_str(json)
}

/// 技グループマスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_move_groups(json: &str) -> Result<Vec<MoveGroups>, serde_json::Error> {
    serde_json::from_str(json)
}

/// 技マスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_moves(json: &str) -> Result<Vec<Moves>, serde_json::Error> {
    serde_json::from_str(json)
}

/// 技グループ所属技の対応表のJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_move_group_moves(json: &str) -> Result<Vec<MoveGroupMoves>, serde_json::Error> {
    serde_json::from_str(json)
}

/// スターター編成マスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_starter_party_slots(json: &str) -> Result<Vec<StarterPartySlots>, serde_json::Error> {
    serde_json::from_str(json)
}

/// `seed_master_data`コマンドが参照する、埋め込み済みパチモンマスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_pachimon_data() -> Vec<Pachimon> {
    parse_pachimon(PACHIMON_JSON).expect("master_data/pachimon.json のパースに失敗しました")
}

/// `seed_master_data`コマンドが参照する、埋め込み済み技グループマスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_move_groups_data() -> Vec<MoveGroups> {
    parse_move_groups(MOVE_GROUPS_JSON)
        .expect("master_data/move_groups.json のパースに失敗しました")
}

/// `seed_master_data`コマンドが参照する、埋め込み済み技マスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_moves_data() -> Vec<Moves> {
    parse_moves(MOVES_JSON).expect("master_data/moves.json のパースに失敗しました")
}

/// `seed_master_data`コマンドが参照する、埋め込み済み技グループ所属技対応表のシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_move_group_moves_data() -> Vec<MoveGroupMoves> {
    parse_move_group_moves(MOVE_GROUP_MOVES_JSON)
        .expect("master_data/move_group_moves.json のパースに失敗しました")
}

/// `seed_master_data`コマンドが参照する、埋め込み済みスターター編成マスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_starter_party_slots_data() -> Vec<StarterPartySlots> {
    parse_starter_party_slots(STARTER_PARTY_SLOTS_JSON)
        .expect("master_data/starter_party_slots.json のパースに失敗しました")
}

/// アイテムマスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_items(json: &str) -> Result<Vec<Items>, serde_json::Error> {
    serde_json::from_str(json)
}

/// `seed_master_data`コマンドが参照する、埋め込み済みアイテムマスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_items_data() -> Vec<Items> {
    parse_items(ITEMS_JSON).expect("master_data/items.json のパースに失敗しました")
}

/// スカウトのバナーマスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_scout_banners(json: &str) -> Result<Vec<ScoutBanners>, serde_json::Error> {
    serde_json::from_str(json)
}

/// スカウトのバナー排出率マスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_scout_banner_rates(json: &str) -> Result<Vec<ScoutBannerRates>, serde_json::Error> {
    serde_json::from_str(json)
}

/// `seed_master_data`コマンドが参照する、埋め込み済みスカウトのバナーマスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_scout_banners_data() -> Vec<ScoutBanners> {
    parse_scout_banners(SCOUT_BANNERS_JSON)
        .expect("master_data/scout_banners.json のパースに失敗しました")
}

/// `seed_master_data`コマンドが参照する、埋め込み済みスカウトのバナー排出率マスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_scout_banner_rates_data() -> Vec<ScoutBannerRates> {
    parse_scout_banner_rates(SCOUT_BANNER_RATES_JSON)
        .expect("master_data/scout_banner_rates.json のパースに失敗しました")
}

/// ショップの商品マスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_shop_products(json: &str) -> Result<Vec<ShopProducts>, serde_json::Error> {
    serde_json::from_str(json)
}

/// `seed_master_data`コマンドが参照する、埋め込み済みショップの商品マスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_shop_products_data() -> Vec<ShopProducts> {
    parse_shop_products(SHOP_PRODUCTS_JSON)
        .expect("master_data/shop_products.json のパースに失敗しました")
}

/// ショップの商品マスタの、パイプラインのvalidateで確かめられない決まりを確かめる。
/// 受け取る数・払う数が1以上で、払うアイテムがジェムであること(購入の失敗をジェム不足の業務エラーで返すため)。
///
/// # Errors
/// 決まりに合わない商品があれば、その`shop_product_id`と理由を返す。
pub fn validate_shop_products(products: &[ShopProducts]) -> Result<(), String> {
    for product in products {
        if product.amount <= 0 || product.cost_amount <= 0 {
            return Err(format!(
                "shop_product_id={}: amountとcost_amountは1以上にする",
                product.shop_product_id
            ));
        }
        if product.cost_item_id != crate::service::player_item_service::GEM_ITEM_ID {
            return Err(format!(
                "shop_product_id={}: cost_item_idはジェム(item_id={})にする",
                product.shop_product_id,
                crate::service::player_item_service::GEM_ITEM_ID
            ));
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// master_data/pachimon.jsonが正しくパースでき、既知の件数分ロードできることを確認する。
    #[test]
    fn test_parse_pachimon_loads_master_data() {
        let pachimon = parse_pachimon(PACHIMON_JSON).expect("パースに成功するはず");
        assert_eq!(pachimon.len(), 136);
    }

    /// 不正なJSONを渡した場合にエラーを返すことを確認する。
    #[test]
    fn test_parse_pachimon_rejects_invalid_json() {
        let result = parse_pachimon("not valid json");
        assert!(result.is_err());
    }

    /// 全パチモンの`pachimon_id`が重複していないことを確認する(マスタデータの整合性検証)。
    #[test]
    fn test_pachimon_ids_are_unique() {
        let pachimon = parse_pachimon(PACHIMON_JSON).unwrap();
        let mut ids: Vec<i32> = pachimon.iter().map(|p| p.pachimon_id).collect();
        ids.sort_unstable();
        ids.dedup();
        assert_eq!(ids.len(), pachimon.len());
    }

    /// 先頭のパチモン(イフリーガ)のタイプ・レアリティが期待通りにデコードされることを確認する。
    #[test]
    fn test_pachimon_type_and_rarity_decoding() {
        let pachimon = parse_pachimon(PACHIMON_JSON).unwrap();
        let ifuriga = &pachimon[0];
        assert_eq!(ifuriga.name, "イフリーガ");
        assert_eq!(ifuriga.primary_type, PachimonType::Fire);
        assert_eq!(ifuriga.secondary_type, PachimonType::Flying);
        assert_eq!(ifuriga.rarity, Rarity::S);
    }

    /// `seed_pachimon_data`経由でも同じデータが取得できることを確認する。
    #[test]
    fn test_seed_pachimon_data_matches_parse() {
        assert_eq!(seed_pachimon_data().len(), 136);
    }

    /// master_data/move_groups.jsonが正しくパースできることを確認する。
    #[test]
    fn test_parse_move_groups_loads_master_data() {
        let move_groups = parse_move_groups(MOVE_GROUPS_JSON).expect("パースに成功するはず");
        assert_eq!(move_groups.len(), 136);
    }

    /// master_data/moves.jsonが正しくパースできることを確認する。
    #[test]
    fn test_parse_moves_loads_master_data() {
        let moves = parse_moves(MOVES_JSON).expect("パースに成功するはず");
        assert_eq!(moves.len(), 261);
    }

    /// master_data/move_group_moves.jsonが正しくパースできることを確認する。
    #[test]
    fn test_parse_move_group_moves_loads_master_data() {
        let rows = parse_move_group_moves(MOVE_GROUP_MOVES_JSON).expect("パースに成功するはず");
        assert_eq!(rows.len(), 4403);
    }

    /// move_group_movesの`unique_id`が重複していないことを確認する(マスタデータの整合性検証)。
    #[test]
    fn test_move_group_moves_unique_ids_are_unique() {
        let rows = parse_move_group_moves(MOVE_GROUP_MOVES_JSON).unwrap();
        let mut ids: Vec<i32> = rows.iter().map(|r| r.unique_id).collect();
        ids.sort_unstable();
        ids.dedup();
        assert_eq!(ids.len(), rows.len());
    }

    /// move_group_movesが参照するgroup_id/move_idが、それぞれmove_groups/movesに
    /// 実在することを確認する(FK制約と同等の整合性をユニットテストレベルでも担保する)。
    #[test]
    fn test_move_group_moves_references_are_valid() {
        let groups = parse_move_groups(MOVE_GROUPS_JSON).unwrap();
        let moves = parse_moves(MOVES_JSON).unwrap();
        let rows = parse_move_group_moves(MOVE_GROUP_MOVES_JSON).unwrap();

        let group_ids: std::collections::HashSet<i32> =
            groups.iter().map(|g| g.move_group_id).collect();
        let move_ids: std::collections::HashSet<i32> = moves.iter().map(|m| m.move_id).collect();

        for row in &rows {
            assert!(
                group_ids.contains(&row.group_id),
                "未知のgroup_id: {}",
                row.group_id
            );
            assert!(
                move_ids.contains(&row.move_id),
                "未知のmove_id: {}",
                row.move_id
            );
        }
    }

    /// 全技グループの初期技(`is_initial`)がちょうど4つであることを確認する。
    /// スターター付与・スカウト入手の個体は、この4つを技slot1〜4にそのままセットする。
    #[test]
    fn test_every_move_group_has_four_initial_moves() {
        let groups = parse_move_groups(MOVE_GROUPS_JSON).unwrap();
        let rows = parse_move_group_moves(MOVE_GROUP_MOVES_JSON).unwrap();

        for group in &groups {
            let initial_count = rows
                .iter()
                .filter(|r| r.group_id == group.move_group_id && r.is_initial)
                .count();
            assert_eq!(
                initial_count, 4,
                "move_group_id={}の初期技が4つではない",
                group.move_group_id
            );
        }
    }

    /// master_data/starter_party_slots.jsonが正しくパースでき、6slot分ロードできることを確認する。
    #[test]
    fn test_parse_starter_party_slots_loads_master_data() {
        let slots =
            parse_starter_party_slots(STARTER_PARTY_SLOTS_JSON).expect("パースに成功するはず");
        assert_eq!(slots.len(), 6);
    }

    /// starter_party_slotsの`slot_no`が重複していないことを確認する(マスタデータの整合性検証)。
    #[test]
    fn test_starter_party_slots_slot_no_are_unique() {
        let slots = parse_starter_party_slots(STARTER_PARTY_SLOTS_JSON).unwrap();
        let mut slot_nos: Vec<i32> = slots.iter().map(|s| s.slot_no).collect();
        slot_nos.sort_unstable();
        slot_nos.dedup();
        assert_eq!(slot_nos.len(), slots.len());
    }

    /// starter_party_slotsが参照するpachimon_idが、pachimonマスタに実在することを確認する
    /// (FK制約と同等の整合性をユニットテストレベルでも担保する)。
    #[test]
    fn test_starter_party_slots_references_are_valid() {
        let pachimon = parse_pachimon(PACHIMON_JSON).unwrap();
        let slots = parse_starter_party_slots(STARTER_PARTY_SLOTS_JSON).unwrap();

        let pachimon_ids: std::collections::HashSet<i32> =
            pachimon.iter().map(|p| p.pachimon_id).collect();

        for slot in &slots {
            assert!(
                pachimon_ids.contains(&slot.pachimon_id),
                "未知のpachimon_id: {}",
                slot.pachimon_id
            );
        }
    }

    /// master_data/items.jsonが正しくパースでき、既知の件数分ロードできることを確認する。
    #[test]
    fn test_parse_items_loads_master_data() {
        let items = parse_items(ITEMS_JSON).expect("パースに成功するはず");
        assert_eq!(items.len(), 2);
        assert_eq!(items[0].item_id, 1);
        assert_eq!(items[0].name, "ジェム");
    }

    /// itemsの`item_id`が重複していないことを確認する(マスタデータの整合性検証)。
    #[test]
    fn test_items_ids_are_unique() {
        let items = parse_items(ITEMS_JSON).unwrap();
        let mut ids: Vec<i32> = items.iter().map(|i| i.item_id).collect();
        ids.sort_unstable();
        ids.dedup();
        assert_eq!(ids.len(), items.len());
    }

    /// 埋め込み済みのショップの商品マスタがパースでき、seed_master_dataの検証を通ることを確認する。
    #[test]
    fn test_shop_products_master_data_is_valid() {
        let products = parse_shop_products(SHOP_PRODUCTS_JSON).expect("パースに成功するはず");
        assert_eq!(products.len(), 3);
        validate_shop_products(&products).expect("検証を通るはず");
    }

    /// 払うアイテムがジェムでない商品・数が0の商品は、検証で弾く。
    #[test]
    fn test_validate_shop_products_rejects_invalid_products() {
        let product = |amount, cost_item_id, cost_amount| ShopProducts {
            shop_product_id: 1,
            item_id: 2,
            amount,
            cost_item_id,
            cost_amount,
        };
        assert!(validate_shop_products(&[product(10, 1, 50)]).is_ok());
        assert!(validate_shop_products(&[product(0, 1, 50)]).is_err());
        assert!(validate_shop_products(&[product(10, 1, 0)]).is_err());
        assert!(validate_shop_products(&[product(10, 2, 50)]).is_err());
    }

    /// 埋め込み済みのスカウトのバナーマスタが、seed_master_dataの検証(日時・期間・重みの合計)を通ることを確認する。
    #[test]
    fn test_scout_banners_master_data_is_valid() {
        let banners = seed_scout_banners_data();
        let rates = seed_scout_banner_rates_data();
        let built = scout_banner::build_scout_banners(&banners, &rates).expect("検証を通るはず");
        assert!(
            built
                .iter()
                .any(|banner| banner.banner_id == "SCOUT000000000000000000001"),
            "常設のレギュラースカウトが無い"
        );
    }
}
