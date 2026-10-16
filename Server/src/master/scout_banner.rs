use std::collections::HashMap;
use std::fmt::{self, Display};

use chrono::NaiveDateTime;

use super::{Rarity, ScoutBannerRates, ScoutBanners};

/// マスタの CSV でバナーの日時を書く形(UTC)。
pub const BANNER_DATETIME_FORMAT: &str = "%Y-%m-%d %H:%M:%S";

/// バナーごとの排出率の重みの合計(1万分率)。
pub const TOTAL_RATE_WEIGHT: i32 = 10_000;

/// スカウトのバナー1件と、その排出率。`scout_banners` と `scout_banner_rates` を組にしたもの。
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ScoutBanner {
    pub banner_id: String,
    pub name: String,
    pub cost_per_roll: i32,
    /// 開催の始まり(UTC)。
    pub start_at: NaiveDateTime,
    /// 開催の終わり(UTC)。
    pub end_at: NaiveDateTime,
    /// レア度ごとの重み。合計は `TOTAL_RATE_WEIGHT`。
    pub rates: Vec<ScoutBannerRate>,
}

impl ScoutBanner {
    /// `now`(UTC)が開催期間に入っているか。始まりと終わりの時刻ちょうども含む。
    pub fn is_open(&self, now: NaiveDateTime) -> bool {
        self.start_at <= now && now <= self.end_at
    }
}

/// バナーの、1つのレア度の重み。
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ScoutBannerRate {
    pub rarity: Rarity,
    /// 1万分率の整数(3% = 300)。
    pub weight: i32,
}

/// バナーのマスタが、パイプラインの検証では確かめられない決まりに合わない。
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct InvalidScoutBanner {
    pub banner_id: String,
    pub reason: String,
}

impl Display for InvalidScoutBanner {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "banner_id={}: {}", self.banner_id, self.reason)
    }
}

impl std::error::Error for InvalidScoutBanner {}

/// マスタの行(日時は文字列)から、バナーと排出率の組を作る。
///
/// # Errors
/// 日時が `BANNER_DATETIME_FORMAT` で読めない、`start_at < end_at` でない、重みが負、
/// 重みの合計が `TOTAL_RATE_WEIGHT` でない(排出率が1件も無い場合を含む)、
/// 同じバナーで同じレア度が2行あるときに `InvalidScoutBanner` を返す。
pub fn build_scout_banners(
    banners: &[ScoutBanners],
    rates: &[ScoutBannerRates],
) -> Result<Vec<ScoutBanner>, InvalidScoutBanner> {
    let mut rates_by_banner: HashMap<&str, Vec<ScoutBannerRate>> = HashMap::new();
    for rate in rates {
        rates_by_banner
            .entry(rate.banner_id.as_str())
            .or_default()
            .push(ScoutBannerRate {
                rarity: rate.rarity,
                weight: rate.weight,
            });
    }

    banners
        .iter()
        .map(|banner| {
            let invalid = |reason: String| InvalidScoutBanner {
                banner_id: banner.banner_id.clone(),
                reason,
            };
            let parse = |value: &str| {
                NaiveDateTime::parse_from_str(value, BANNER_DATETIME_FORMAT).map_err(|err| {
                    invalid(format!(
                        "{value:?} is not a datetime in {BANNER_DATETIME_FORMAT}: {err}"
                    ))
                })
            };
            let start_at = parse(&banner.start_at)?;
            let end_at = parse(&banner.end_at)?;
            if start_at >= end_at {
                return Err(invalid("start_at must be before end_at".to_owned()));
            }

            let rates = rates_by_banner
                .remove(banner.banner_id.as_str())
                .unwrap_or_default();
            validate_rates(&rates).map_err(invalid)?;

            Ok(ScoutBanner {
                banner_id: banner.banner_id.clone(),
                name: banner.name.clone(),
                cost_per_roll: banner.cost_per_roll,
                start_at,
                end_at,
                rates,
            })
        })
        .collect()
}

fn validate_rates(rates: &[ScoutBannerRate]) -> Result<(), String> {
    if let Some(rate) = rates.iter().find(|rate| rate.weight < 0) {
        return Err(format!("weight of {:?} is negative", rate.rarity));
    }
    for (index, rate) in rates.iter().enumerate() {
        if rates[..index].iter().any(|r| r.rarity == rate.rarity) {
            return Err(format!("rarity {:?} appears twice", rate.rarity));
        }
    }
    let total: i32 = rates.iter().map(|rate| rate.weight).sum();
    if total != TOTAL_RATE_WEIGHT {
        return Err(format!(
            "total weight is {total}, expected {TOTAL_RATE_WEIGHT}"
        ));
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn banner(start_at: &str, end_at: &str) -> ScoutBanners {
        ScoutBanners {
            banner_id: "B1".to_owned(),
            name: "バナー".to_owned(),
            cost_per_roll: 150,
            start_at: start_at.to_owned(),
            end_at: end_at.to_owned(),
        }
    }

    fn rate(id: i32, rarity: Rarity, weight: i32) -> ScoutBannerRates {
        ScoutBannerRates {
            scout_banner_rate_id: id,
            banner_id: "B1".to_owned(),
            rarity,
            weight,
        }
    }

    fn full_rates() -> Vec<ScoutBannerRates> {
        vec![
            rate(1, Rarity::S, 300),
            rate(2, Rarity::A, 1200),
            rate(3, Rarity::B, 3500),
            rate(4, Rarity::C, 5000),
        ]
    }

    /// 正しい行から、日時を読んだバナーと排出率の組ができる。
    #[test]
    fn builds_banner_with_rates() {
        let banners = [banner("2026-01-01 00:00:00", "2126-01-01 00:00:00")];
        let built = build_scout_banners(&banners, &full_rates()).unwrap();

        assert_eq!(built.len(), 1);
        assert_eq!(built[0].rates.len(), 4);
        assert_eq!(
            built[0].start_at,
            NaiveDateTime::parse_from_str("2026-01-01 00:00:00", BANNER_DATETIME_FORMAT).unwrap()
        );
    }

    /// 開催期間は始まりと終わりの時刻ちょうども含む。
    #[test]
    fn is_open_includes_both_ends() {
        let banners = [banner("2026-01-01 00:00:00", "2026-02-01 00:00:00")];
        let built = &build_scout_banners(&banners, &full_rates()).unwrap()[0];
        let at = |value| NaiveDateTime::parse_from_str(value, BANNER_DATETIME_FORMAT).unwrap();

        assert!(!built.is_open(at("2025-12-31 23:59:59")));
        assert!(built.is_open(at("2026-01-01 00:00:00")));
        assert!(built.is_open(at("2026-02-01 00:00:00")));
        assert!(!built.is_open(at("2026-02-01 00:00:01")));
    }

    /// 日時として読めない値・始まりが終わりより後のバナーは受け付けない。
    #[test]
    fn rejects_invalid_period() {
        for (start_at, end_at) in [
            ("2026/01/01 00:00:00", "2126-01-01 00:00:00"),
            ("2026-01-01", "2126-01-01 00:00:00"),
            ("2026-01-01 00:00:00", "2026-01-01 00:00:00"),
            ("2026-02-01 00:00:00", "2026-01-01 00:00:00"),
        ] {
            let banners = [banner(start_at, end_at)];
            assert!(
                build_scout_banners(&banners, &full_rates()).is_err(),
                "{start_at} - {end_at}"
            );
        }
    }

    /// 重みの合計が10000でない・排出率が無い・同じレア度が2行あるバナーは受け付けない。
    #[test]
    fn rejects_invalid_rates() {
        let banners = [banner("2026-01-01 00:00:00", "2126-01-01 00:00:00")];
        let cases = [
            vec![rate(1, Rarity::S, 300), rate(2, Rarity::C, 5000)],
            vec![],
            vec![rate(1, Rarity::C, 5000), rate(2, Rarity::C, 5000)],
            vec![rate(1, Rarity::S, -1), rate(2, Rarity::C, 10_001)],
        ];
        for rates in cases {
            assert!(build_scout_banners(&banners, &rates).is_err(), "{rates:?}");
        }
    }
}
