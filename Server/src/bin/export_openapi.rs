// handlerの#[utoipa::path]注釈から生成したOpenAPI仕様書を
// Shared/api/openapi.yaml へ書き出すコマンド。
//
// api-codegen(Unity C#側のDTO/通信APIクラス生成、Shared/docs/feature-api-codegen.md参照)
// の入力となる想定。api-codegen自体は未実装で、現時点ではこのyamlは
// Swagger UI(/swagger-ui)と同じ仕様書をファイルとして確認する用途。
//
// 実行方法: cargo run --bin export_openapi (Server/ が作業ディレクトリである前提)
use Server::openapi::ApiDoc;
use utoipa::OpenApi;

const OUTPUT_PATH: &str = "../Shared/api/openapi.yaml";
const DO_NOT_EDIT_HEADER: &str =
    "# DO NOT EDIT - `cargo run --bin export_openapi` (Server/) が自動生成するファイルです\n";

fn main() {
    let yaml = ApiDoc::openapi()
        .to_yaml()
        .expect("OpenAPI仕様書のYAMLシリアライズに失敗しました");

    std::fs::create_dir_all("../Shared/api").expect("Shared/api ディレクトリの作成に失敗しました");
    std::fs::write(OUTPUT_PATH, format!("{DO_NOT_EDIT_HEADER}{yaml}"))
        .unwrap_or_else(|err| panic!("{OUTPUT_PATH} への書き込みに失敗しました: {err}"));

    println!("{OUTPUT_PATH} を生成しました");
}
