pub struct Device {
    pub device_id: String,
    pub player_id: String,
    pub secret_key_hash: String,
    pub created_at: chrono::NaiveDateTime,
}