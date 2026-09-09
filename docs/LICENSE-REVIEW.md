# Distribution review — NOT a completed legal clearance

Rune's own source is licensed under GPLv3-only with the Valheim/Unity linking exception, as approved by its author. See the source package's `LICENSE` and `LINKING-EXCEPTION.md`. This candidate remains for local evaluation until the remaining third-party corresponding-source obligations are resolved.

The package includes exact package metadata and available upstream license files. Code, models, native libraries and voice assets can have separate terms. See `audio-components.json`, the expressive pack's `*.dist-info` metadata, Python's LICENSE.txt and .NET's notices.

Sources checked 8 September 2026:
- Python Windows embedded distribution: https://www.python.org/downloads/release/python-31210/
- .NET distribution and notices: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- Kokoro model/voices (Apache 2.0): https://huggingface.co/hexgrad/Kokoro-82M
- kokoro-onnx: https://github.com/thewh1teagle/kokoro-onnx
- Faster Whisper: https://github.com/SYSTRAN/faster-whisper
- Whisper small.en conversion: https://huggingface.co/Systran/faster-whisper-small.en
- Chatterbox, Turbo and multilingual V3: https://github.com/resemble-ai/chatterbox and https://www.resemble.ai/learn/models/chatterbox
- Qwen3.5-4B: https://huggingface.co/Qwen/Qwen3.5-4B
- Ollama: https://github.com/ollama/ollama
- Codex app-server/authentication: https://learn.chatgpt.com/docs/app-server and https://learn.chatgpt.com/docs/auth

Remaining public-release gate: audit corresponding-source and redistribution obligations for all native speech dependencies (including phonemizer/eSpeak and FFmpeg/PyAV), model files and optional CUDA libraries. The presence of a license file does not by itself discharge GPL/LGPL/source obligations. Do not mark the public candidate legally cleared until exact dependency versions and required source distribution are resolved.

Confirmed finding: the installed phonemizer 3.4.0 metadata declares GPLv3, and the current Rune audio service reaches it through kokoro-onnx. A public-source release can address the source-sharing requirement, but merely putting files in a separate ZIP or publishing only Rune's source does not resolve every native dependency's corresponding-source obligation. Include exact source/build information for the native dependencies and review the expressive stack before distribution.

No Valheim assemblies, game assets, other players' profiles, obsolete Piper voice or Llama model are selected by the release builder. The standalone plugin is Rune's DLL; install third-party Valheim mods through their original Thunderstore packages instead of copying the author's mod collection.

The ChatGPT helper is user-installed, not redistributed in this candidate. The integration uses app-server sign-in; it supplies no tool-execution approval callback and does not read account tokens. Do not promise unlimited access or a cloud voice entitlement. Confirm service terms and account eligibility when enabling this optional integration.

Valheim requires clear unofficial identification and discourages paywalled mods while allowing voluntary support: https://www.valheimgame.com/news/regarding-mods/
