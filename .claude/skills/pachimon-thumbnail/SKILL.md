---
name: pachimon-thumbnail
description: パチモンの仮のサムネイル(主タイプの色の円にpachimon_idを書いた画像)を、まだ画像が無いIDについて生成し、Addressablesに登録するときに使う。pachimonマスタ(pachimon_master.csv)にパチモンを追加した後や、サムネイルが足りない・Addressablesに載っていないときに実行する。
---

# パチモンのサムネイル生成

正式な画像が用意されるまでの仮のサムネイルを作る。生成はUnityエディタ上のEditorスクリプト
(`Client/AtlasUnityProject/Assets/Scripts/Editor/Thumbnail/PachimonThumbnailGenerator.cs`)が行う。
文字をプロジェクトのTMPフォントで描くため、Unityエディタが起動している必要がある(uloopで呼ぶ)。

## 生成物

| 項目 | 内容 |
|---|---|
| 置き場所 | `Client/AtlasUnityProject/Assets/Addressables/Thumbnail/{id}/pachimon_{id}_thumb.png` |
| 見た目 | 256×256。主タイプの色(`Assets/Settings/PachimonTypePalette.asset`)の円に、白(明るい色の上では黒)でID |
| 取り込み | Sprite・ミップマップ無し・最大256(`PachimonThumbnailImporter`が初回の取り込み時に設定) |
| Addressables | グループ`Thumbnail`。アドレスはファイル名(`pachimon_{id}_thumb`)、ラベル`thumbnail_{id}`。グループは`Pack Together By Label`なので、IDごとに1つのアセットバンドルになる |

- IDの一覧は正本の`Shared/master-data/csv/pachimon_master.csv`から読む(マスタの生成物ではない)
- **既にある画像は上書きしない**。正式な画像に差し替えたファイルもそのまま残る
- 読み込みのアドレスは`AddressDefinition.GetThumb(id)`。IDごとの定数は作らない
- 画面側はPresenterがDTOに`pachimon_id`を入れ、View(`PachimonThumbnailView`・`PachimonFigureView`)が`IAssetLoader`で読み込む

## 手順

作業ディレクトリは`Client/AtlasUnityProject`。

1. Unityエディタが起動していることを確かめる(起動していなければ`uloop launch`)。
   C#を変えた直後なら`uloop compile`でエラーが無いことを確かめる
2. 足りないサムネイルを生成して、Addressablesに登録する

   ```bash
   uloop execute-dynamic-code --code 'return Atlas.Editor.Thumbnail.PachimonThumbnailGenerator.GenerateMissing();'
   ```

   `Result`に生成したIDの一覧と、Addressablesのエントリ数が返る。
   例: `Generated 2 thumbnails: 1037, 1038 (skipped 36 existing, 38 addressable entries)`。
   足りないものが無いときは`No thumbnails to generate (...)`になる(登録だけ当て直す)
3. 生成した画像を1枚`Read`で開いて見た目を確かめる
4. `git status`で、次のファイルが増えていることを確かめる
   - `Assets/Addressables/Thumbnail/{id}/`(フォルダの`.meta`、画像と`.meta`)
   - `Assets/AddressableAssetsData/AssetGroups/Thumbnail.asset`(エントリの追加)
   - `Assets/AddressableAssetsData/AddressableAssetSettings.asset`(ラベルの追加)

Unityのメニュー`Tools > Generate Pachimon Thumbnails`でも同じ処理を実行できる。

### 全部作り直すとき

見た目(色・文字の大きさ等)を変えたときだけ使う。**正式な画像に差し替えたファイルも上書きされる**ので、
差し替え済みのものがある場合は使わない。

```bash
uloop execute-dynamic-code --code 'return Atlas.Editor.Thumbnail.PachimonThumbnailGenerator.RegenerateAll();'
```

## 注意

- SmartAddresserの`Primary Data`はプロジェクトで設定しておらず、画像を取り込んでも自動ではAddressablesに
  登録されない。生成スクリプトは`Thumbnail`フォルダの画像にだけレイアウトルール(`Assets/LayoutRuleData.asset`)を
  当てる。**正式な画像を手で置いたときも`GenerateMissing()`を実行すれば登録される**
- 画像は`.png`をスクリプトで書き出すだけで、prefab・アセットのYAMLは書き換えない
- タイプの色の元は`PachimonTypePalette`。CSVの`primary_type`(表示名)を`PachimonTypeNames`で`PachimonType`に戻して引く。
  表示名が見つからないタイプは灰色(`None`の色)になる
