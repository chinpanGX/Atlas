pub struct Player {
    pub player_id: String,
    pub device_id: String,
    pub nickname: String,
    pub gems: i32,
    pub created_at: chrono::NaiveDateTime,
}
