# REE-Content-Exporter

REE-Content-Exporter is a small export wizard built on top of REE-Content-Editor / RE-Engine-Lib. It helps move supported RE Engine meshes, materials, textures, skeletons, and animations into Blender, Unreal, or other DCC/game-engine workflows.

Version 0.4 makes the wizard game-configurable. It is no longer a PRAGMATA-only workflow.

## English

### What You Need

- A loose-file extract of the RE Engine game you want to export from.
- Blender 4.5.9 LTS when using the Unreal-ready FBX workflow.
- The release package of REE-Content-Exporter, or a local developer build prepared with the matching REE-Content-Editor dependency.

### First Run

Open `REE-Content-Exporter.exe`.

The wizard asks for language, then asks which game configuration to use. Choose the game from the numbered list. The wizard downloads the matching `.list` file from Ekey's REE.PAK.Tool repository and saves that choice in `config.json`.

After a game is saved, the wizard does not ask again. Each run prints the current game configuration before any other prompts, for example:

`Current game configuration: Pragmata (delete the "game" line from config.json to set a different game)`

To change games later, delete only the `game` line from the config file and run the wizard again.

### Normal Workflow

1. Select or confirm the game extract folder.
2. Select or confirm the export folder.
3. Select or confirm Blender 4.5.9 LTS.
4. Choose a single mesh export or CSV batch export.
5. Pick meshes by path, filename, or search text from the selected game's downloaded list.
6. For skeletal meshes, choose whether to include MOTLIST animations.
7. Let the wizard generate the PowerShell export script.
8. Run the generated script when ready.

Generated scripts write logs and reports into the export folder. Successful logs end in `-SUCCESS.log`; failed logs end in `-FAIL.log`.

### Detailed Reference

The full command-line reference, game ID table, release packaging notes, and AI-agent operating details have moved to [docs/ai_cli_reference.md](docs/ai_cli_reference.md). Human users can read it too, but it is intentionally detailed and primarily meant for AI agents and advanced maintenance work.

## 한국어

### 필요한 것

- 내보낼 RE Engine 게임의 loose-file 추출 폴더.
- Unreal-ready FBX 워크플로를 사용할 경우 Blender 4.5.9 LTS.
- REE-Content-Exporter 릴리스 패키지 또는 일치하는 REE-Content-Editor 의존성이 준비된 로컬 개발 빌드.

### 첫 실행

`REE-Content-Exporter.exe`를 실행하세요.

마법사는 언어를 물어본 뒤 사용할 게임 구성을 물어봅니다. 번호 목록에서 게임을 선택하세요. 마법사는 Ekey의 REE.PAK.Tool 저장소에서 해당 `.list` 파일을 다운로드하고 선택한 게임을 `config.json`에 저장합니다.

게임이 저장된 뒤에는 다시 묻지 않습니다. 이후 마법사를 실행할 때마다 다음과 같은 현재 게임 구성을 먼저 출력합니다.

`현재 게임 구성: Pragmata (다른 게임을 설정하려면 config.json 파일에서 "game" 줄을 삭제하세요)`

나중에 다른 게임으로 바꾸려면 config 파일에서 `game` 줄만 삭제한 뒤 마법사를 다시 실행하세요.

### 일반 사용 흐름

1. 게임 추출 폴더를 선택하거나 확인합니다.
2. 내보내기 폴더를 선택하거나 확인합니다.
3. Blender 4.5.9 LTS 경로를 선택하거나 확인합니다.
4. 단일 메시 내보내기 또는 CSV 배치 내보내기를 선택합니다.
5. 선택한 게임의 다운로드된 목록에서 경로, 파일명, 검색어로 메시를 고릅니다.
6. 스켈레탈 메시라면 MOTLIST 애니메이션 포함 여부를 선택합니다.
7. 마법사가 PowerShell 내보내기 스크립트를 생성하도록 합니다.
8. 준비되면 생성된 스크립트를 실행합니다.

생성된 스크립트는 로그와 보고서를 내보내기 폴더에 기록합니다. 성공 로그는 `-SUCCESS.log`, 실패 로그는 `-FAIL.log`로 끝납니다.

### 자세한 참고 문서

전체 명령줄 참고, 게임 ID 표, 릴리스 패키징 메모, AI 에이전트 작업 세부사항은 [docs/ai_cli_reference.md](docs/ai_cli_reference.md)로 이동했습니다. 사람도 읽을 수 있지만, 내용이 복잡하기 때문에 주된 대상은 AI 에이전트와 고급 유지보수 작업입니다.
