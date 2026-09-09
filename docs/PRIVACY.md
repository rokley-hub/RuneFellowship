# Rune data and privacy

Rune does not automatically upload diagnostics to its developer.

Microphone recognition and companion speech synthesis run on this PC. Live microphone capture is held in memory by the audio service. Recognized words, companion replies, profiles, memories and bounded diagnostic session records can be written in the installation's `bridge` folder. Test tools can explicitly create synthetic WAV files; these are not live microphone recordings.

In Local mode, the local Qwen service processes conversations. In Split or Full ChatGPT mode, relevant text, companion personality, conversation context and game-state facts are sent to OpenAI through the account helper. OpenAI's applicable terms and account data controls govern that processing. Local audio does not mean cloud dialogue is private to this PC.

ChatGPT login is handled by the official helper using a separate account-data folder. Never share `bridge/chatgpt-account`, your full bridge folder, exported personal mod configs without review, or a ZIP of your complete installation. Rune does not bundle the author's account.

Thunderstore receives requests when Rune retrieves the mod catalogue, downloads or opens mod pages. Model providers receive requests for model downloads. PayPal is contacted only when you open the support link. These services have their own privacy policies.

To mute input, use the microphone control or your configured key. Disable spoken replies to retain commands with lower voice resource use. To remove local conversation data, close Rune and its services and remove only the data you intend to discard; uninstall retains it by default. Game saves belong to Valheim and are separate.

For feedback, use the testing template and share only reviewed, relevant excerpts. No background telemetry or automatic support uploads are enabled by this release package.
