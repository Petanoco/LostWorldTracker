## 設定ファイル(appsettings.json)
- api_inter_request_delay: API呼び出し毎の間隔(ミリ秒) あまり短いと怒られるかもしれない.
- output_format: json/csv のどちらかを指定できます.
- private_world_only: `true`にすると、Privateワールドの情報のみを出力します. `false`だとPublic, Private, 削除済ワールド全て出力します.

## 起動引数
何も渡さない or `0`を渡すと通常処理.
`1`を渡すとログアウト処理をします. これいる？