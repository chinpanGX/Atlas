pub struct Message {
    pub message_id: String,
    pub player_id: String,
    pub content: String,
    pub created_at: chrono::NaiveDateTime,
}
