pub mod cache;
pub mod generated;

pub use cache::MasterData;
pub use generated::{Pachimon, PachimonType, Rarity};

/// master-data-pipelineが生成したパチモンマスタのJSON。
///
/// ビルド時にバイナリへ埋め込む(実行時のファイル読み込みやカレントディレクトリに
/// 依存させないため)。このJSON自体もmaster-data-pipelineの生成物であり、直接編集しない。
///
/// APIサーバーが起動時に参照するマスタデータの実体はMySQL(`cache::MasterData::load`)
/// であり、このJSONは`seed_master_data`コマンドがDBへ投入する際のシードデータとして
/// 利用する(詳細は`Shared/docs/design/architecture.md`の「マスターデータ運用」を参照)。
const PACHIMON_JSON: &str = include_str!("../../master_data/pachimon.json");

/// パチモンマスタのJSON文字列をパースする。
///
/// # Errors
/// JSONの形式が不正な場合に`serde_json::Error`を返す。
pub fn parse_pachimon(json: &str) -> Result<Vec<Pachimon>, serde_json::Error> {
    serde_json::from_str(json)
}

/// `seed_master_data`コマンドが参照する、埋め込み済みパチモンマスタのシードデータ。
///
/// # Panics
/// 埋め込み済みJSONのパースに失敗した場合(ビルド成果物の不整合)にpanicする。
pub fn seed_pachimon_data() -> Vec<Pachimon> {
    parse_pachimon(PACHIMON_JSON).expect("master_data/pachimon.json のパースに失敗しました")
}

#[cfg(test)]
mod tests {
    use super::*;

    /// master_data/pachimon.jsonが正しくパースでき、既知の件数分ロードできることを確認する。
    #[test]
    fn test_parse_pachimon_loads_master_data() {
        let pachimon = parse_pachimon(PACHIMON_JSON).expect("パースに成功するはず");
        assert_eq!(pachimon.len(), 18);
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
        assert_eq!(seed_pachimon_data().len(), 18);
    }
}
