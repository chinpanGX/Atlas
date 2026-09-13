pub struct Message {
    pub id: i64,
    pub sender_player_id: String,
    pub content: String,
    pub created_at: chrono::NaiveDateTime,
}