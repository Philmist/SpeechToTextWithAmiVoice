# SpeechToTextWithAmiVoice

このプログラムは自分用の「ゆかりねっと」のような音声認識読み上げプログラムです。

以下に挙げる技術の検証目的で作成されました。

- Avalonia ( https://avaloniaui.net/ )
- AmiVoice Cloud Platform ( https://acp.amivoice.com/main/ )
- NAudio ( https://github.com/naudio/NAudio )

エラー処理はほとんどなされていないので検証用と割りきっていただけると幸いです。

## 使用方法

前提としてAmiVoice Cloud Platformの従量課金契約が必要です。
音声認識エンジンは日本語-汎用(`-a-general`)を使用しています。

WebSocketのURI(マイページの接続情報)とAPPKEY(同じく接続情報)を入力して、
マイクを選べばとりあえず音声認識出来るようになります。
APPKEYは他人に漏れてしまうと無効化が難しいようなので注意してください。
WASAPIを前提にしているため、選択したマイクによっては音声を取りこめないことがあります。

Profile IDは単語を登録した場合に必要となります。
マニュアルにもありますがWebから単語登録した場合はProfile IDを設定してください。
ユーザーID(マイページのご登録内容)が`hogehuga`の場合、
Profile IDは`:hogehuga`と設定してください。

## ゆかりねっと的に使うための機能

音声読み上げは棒読みちゃん( https://chi.usamimi.info/Program/Application/BouyomiChan/ )を
使用して読み上げます。
棒読みちゃん側で通常のSocketでの入力を有効化してアドレスとポートを該当箇所に入力してください。
初期状態は棒読みちゃんのデフォルトの設定になっています。

TextSend UriにHTTP POSTで受けたいアドレスを指定すると、
そのアドレスに対してJSONを送信します。
具体的には`{"code": "R", "text": "認識したテキスト" }`のようなJSONが投げられます。
nodecgで使いたい方向けの仕様です。

## その他

今のところ、IssueやPull Requestに対応する予定はありません。

ライセンスはMITです。
条文はLICENCEをごらんください。