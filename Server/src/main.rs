mod api;
mod model;
mod routes;
mod service;
mod state;
use state::AppState;

const DEFAULT_SERVER_ADDR: &str = "127.0.0.1:3000";

#[tokio::main]
async fn main() {
    dotenvy::dotenv().ok();

    let database_url = std::env::var("DATABASE_URL").expect("DATABASE_URL must be set in .env");
    let server_addr = std::env::var("SERVER_ADDR").unwrap_or_else(|_| DEFAULT_SERVER_ADDR.to_string());

    let state = AppState::new(&database_url).await;
    let app = routes::create_router(state);

    let listener = tokio::net::TcpListener::bind(&server_addr).await.unwrap();

    println!("Server running on http://{}", server_addr);
    axum::serve(listener, app).await.unwrap();
}
