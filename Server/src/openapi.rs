use utoipa::{
    Modify, OpenApi,
    openapi::security::{HttpAuthScheme, HttpBuilder, SecurityScheme},
};

use crate::api::{auth, chat, device, player, scout};

/// `Authorization: Bearer <access_token>`によるBearer認証を、
/// OpenAPI仕様書上のセキュリティスキーム`bearer_auth`として登録する。
/// 実際の検証は`AuthenticatedDevice`extractor(axum側)が行い、
/// ここはあくまで仕様書上の表現(Swagger UIの「Authorize」ボタン等)のため。
struct SecurityAddon;

impl Modify for SecurityAddon {
    fn modify(&self, openapi: &mut utoipa::openapi::OpenApi) {
        if let Some(components) = openapi.components.as_mut() {
            components.add_security_scheme(
                "bearer_auth",
                SecurityScheme::Http(
                    HttpBuilder::new()
                        .scheme(HttpAuthScheme::Bearer)
                        .bearer_format("access_token")
                        .build(),
                ),
            );
        }
    }
}

#[derive(OpenApi)]
#[openapi(
    paths(
        device::register_device_handler,
        device::authenticate_device_handler,
        auth::verify_token_handler,
        player::create_player_handler,
        player::get_me_handler,
        chat::send_message_handler,
        chat::poll_messages_handler,
        scout::list_banners_handler,
        scout::create_roll_handler,
        scout::select_roll_handler,
    ),
    components(schemas(
        device::RegisterDeviceRequest,
        device::RegisterDeviceResponse,
        device::AuthenticateDeviceRequest,
        device::AuthenticateDeviceResponse,
        player::CreatePlayerRequest,
        player::PlayerResponse,
        chat::SendMessageRequest,
        chat::SendMessageResponse,
        chat::MessageDto,
        chat::PollMessagesResponse,
        scout::BannerDto,
        scout::ListBannersResponse,
        scout::IvsDto,
        scout::CandidateDto,
        scout::CreateRollRequest,
        scout::CreateRollResponse,
        scout::SelectRollRequest,
        scout::SelectRollResponse,
    )),
    modifiers(&SecurityAddon),
    tags(
        (name = "device", description = "デバイス登録・認証"),
        (name = "auth", description = "アクセストークン検証"),
        (name = "player", description = "プレイヤー管理"),
        (name = "chat", description = "チャット"),
        (name = "scout", description = "スカウト(ガチャ)"),
    ),
)]
pub struct ApiDoc;
