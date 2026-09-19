pub struct AccessToken {
    pub device_id: String,
    pub access_token: String,
    pub expires_at: chrono::NaiveDateTime,
    pub created_at: chrono::NaiveDateTime,
}
