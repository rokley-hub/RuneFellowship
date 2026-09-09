# Original-source installer review — 9 September 2026

The online installer supplies Rune's code and generated assets. It downloads selected third-party runtimes, libraries and model weights from their publishers. It does not bundle other authors' game mods, copy game assets, or supply Valheim. Downloading from the original source reduces redistribution concerns; it does not remove licence obligations or establish that every use is lawful.

Verified model declarations:

- Whisper small.en conversion: MIT — https://huggingface.co/Systran/faster-whisper-small.en
- Kokoro 82M: Apache 2.0 — https://huggingface.co/hexgrad/Kokoro-82M
- Chatterbox Turbo and multilingual: MIT — https://huggingface.co/ResembleAI/chatterbox-turbo and https://huggingface.co/ResembleAI/chatterbox
- Qwen 3.5 4B: Apache 2.0 — https://huggingface.co/Qwen/Qwen3.5-4B

The source lock records each component's exact URL, version and checksum. Downloaded wheels retain their packaged licence/notice files. Original archives remain in the setup cache. Rune's GPLv3 licence and Valheim/Unity linking exception remain applicable. GPL speech components such as phonemizer/eSpeak require their own notices and source obligations to be considered; permissive model licences do not cover every native library. This is a release engineering review, not legal clearance or a promise of zero copyright risk.

The new installer illustration was AI-generated specifically for Rune: an original adult Norse shieldmaiden and landscape, without official Valheim logos, screenshots or copied character art. Avoid claims of official affiliation. Generation alone does not guarantee exclusivity, copyright protection or non-infringement. Use Nexus's relevant AI disclosure tags for artwork and generated content.

Nexus's file submission guidelines request staff contact/review for a tool whose functionality depends on sending or receiving files. This online installer falls within that scope. A review request was sent on 10 September 2026; a response is pending. Do not treat successful local testing or a release on another host as Nexus approval.

Policy source: https://help.nexusmods.com/article/28-file-submission-guidelines

This online variant does not redistribute the downloaded Python, .NET runtimes, speech engines, model weights or optional GPU libraries. Their pinned original downloads retain upstream notices; earlier reviews of bundled offline runtime packages do not authorize publishing those older packages. Rune's distributed application files include the Microsoft System.Speech library with its supplied notices. Include matching Rune source and licence notices, and accurately disclose AI-generated artwork. Nexus review remains a separate host-specific step. Use original mod pages/download flows rather than rehosting other authors' files or bypassing host restrictions.

## ChatGPT integration and product references

Rune starts the separately installed Codex helper using the documented app-server interface and uses its managed ChatGPT sign-in. OpenAI's documentation explicitly describes app-server for integration inside other products and documents the managed OAuth flow. Rune does not scrape the ChatGPT website, supply an account, read OAuth tokens, or bypass usage limits. The user's own eligible account and terms apply. This is evidence supporting the chosen integration route, not an endorsement of Rune or unlimited service entitlement.

- Integration and authentication: https://learn.chatgpt.com/docs/app-server
- Subscription versus developer API authentication: https://learn.chatgpt.com/docs/auth
- Branding: https://openai.com/brand/

Use accurate descriptive wording, for example: "Optional ChatGPT connection through Codex; your account and usage limits apply." Keep Rune's own branding primary. Do not name Rune as a GPT/OpenAI product, imply sponsorship, or use logos as partnership badges. Other software names identify optional compatible components; each project's licences, trademarks and download-host terms remain applicable. The installer currently does not bundle the Codex helper.
