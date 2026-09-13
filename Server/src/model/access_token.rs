pub struct AccessToken {
    pub token: String,
    pub device_id: String,
    pub expires_at: chrono::NaiveDateTime,
    pub created_at: chrono::NaiveDateTime,
}