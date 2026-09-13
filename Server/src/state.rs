use sqlx::MySqlPool;

#[derive(Clone)]
pub struct AppState {
    pub pool: MySqlPool,
}

impl AppState {
    pub async fn new(database_url: &str) -> Self {
        let pool = MySqlPool::connect(database_url)
            .await
            .expect("Failed to connect to database");

        AppState { pool }
    }
}
