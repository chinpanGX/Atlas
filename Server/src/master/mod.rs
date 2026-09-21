pub mod cache;
pub mod generated;

pub use cache::MasterData;
pub use generated::{MoveGroupMoves, MoveGroups, Moves, Pachimon, PachimonType, Rarity};

/// master-data-pipelineが生成した各種マスタのJSON。
///
/// ビルド時にバイナリへ埋め込む(実行時のファイル読み込みやカレントディレクトリに
/// 依存させないため)。これらのJSON自体もmaster-data-pipelineの生成物であり、直接編集しない。
///
/// APIサーバーが起動時に参照するマスタデータの実体はMySQL(`cache::MasterData::load`)
/// であり、これらのJSONは`seed_master_data`コマンドがDBへ投入する際のシードデータとして
/// 利用する(詳細は`Shared/docs/design/architecture.md`の「マスターデータ運用」を参照)。
const PACHIMON_JSON: &str = include_str!("../../master_data/pachimon.json");
const MOVE_GROUPS_JSON: &str = include_str!("../../master_data/move_groups.json");
const MOVES_JSON: &str = include_str!("../../master_data/moves.json");
const MOVE_GROUP_MOVES_JSON: &str = include_str!("../../master_data/move_group_moves.json");

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
    parse_move_groups(MOVE_GROUPS_JSON).expect("master_data/move_groups.json のパースに失敗しました")
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

#[cfg(test)]
mod tests {
    use super::*;

    /// master_data/pachimon.jsonが正しくパースでき、既知の件数分ロードできることを確認する。
    #[test]
    fn test_parse_pachimon_loads_master_data() {
        let pachimon = parse_pachimon(PACHIMON_JSON).expect("パースに成功するはず");
        assert_eq!(pachimon.len(), 36);
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
        let mut ids: Vec<i64> = pachimon.iter().map(|p| p.pachimon_id).collect();
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
        assert_eq!(seed_pachimon_data().len(), 36);
    }

    /// master_data/move_groups.jsonが正しくパースできることを確認する。
    #[test]
    fn test_parse_move_groups_loads_master_data() {
        let move_groups = parse_move_groups(MOVE_GROUPS_JSON).expect("パースに成功するはず");
        assert_eq!(move_groups.len(), 36);
    }

    /// master_data/moves.jsonが正しくパースできることを確認する。
    #[test]
    fn test_parse_moves_loads_master_data() {
        let moves = parse_moves(MOVES_JSON).expect("パースに成功するはず");
        assert_eq!(moves.len(), 38);
    }

    /// master_data/move_group_moves.jsonが正しくパースできることを確認する。
    #[test]
    fn test_parse_move_group_moves_loads_master_data() {
        let rows = parse_move_group_moves(MOVE_GROUP_MOVES_JSON).expect("パースに成功するはず");
        assert_eq!(rows.len(), 108);
    }

    /// move_group_movesの`unique_id`が重複していないことを確認する(マスタデータの整合性検証)。
    #[test]
    fn test_move_group_moves_unique_ids_are_unique() {
        let rows = parse_move_group_moves(MOVE_GROUP_MOVES_JSON).unwrap();
        let mut ids: Vec<i64> = rows.iter().map(|r| r.unique_id).collect();
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

        let group_ids: std::collections::HashSet<i64> =
            groups.iter().map(|g| g.move_group_id).collect();
        let move_ids: std::collections::HashSet<i64> = moves.iter().map(|m| m.move_id).collect();

        for row in &rows {
            assert!(group_ids.contains(&row.group_id), "未知のgroup_id: {}", row.group_id);
            assert!(move_ids.contains(&row.move_id), "未知のmove_id: {}", row.move_id);
        }
    }
}
