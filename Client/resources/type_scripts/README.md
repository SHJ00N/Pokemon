# 타입별 컬러 문자 아트 시안

18개 타입을 `█`와 공백, ANSI 24비트 전경색으로 표현한 파일입니다.
기존 `pokemon_scripts`와 같은 형식이며, 공격할 때 기술의 타입 ID에 맞는 이미지를 불러옵니다.
파일은 빌드·배포 결과물에 함께 복사됩니다. 원래 크기로 표시하고, 전투 그림 영역이 작으면 비율을 유지해 축소합니다.
상대방의 공격 이미지는 좌우로 뒤집어 표시합니다.

- 그림 크기: 16×16픽셀, 콘솔에서는 32글자×16줄
- 한 픽셀: 같은 색의 `██`; 빈 픽셀: 공백 두 글자
- 인코딩: UTF-8 (BOM 없음)
- 색상: `ESC[38;2;R;G;Bm`, 각 줄 끝에서 `ESC[0m`으로 초기화
- 배경색은 지정하지 않아 터미널 배경이 그대로 보입니다.
- 파일명: `{타입 ID 두 자리}_attack_image.txt` (예: 불꽃 타입 10 → `10_attack_image.txt`)
- 파일 번호는 DB 타입 ID와 같습니다. 기본색은 `Server/tools/db_custom/type_colors.csv`를 따릅니다.

C#에서는 타입 ID로 파일명을 만들 수 있습니다:

```csharp
string fileName = $"{typeId:D2}_attack_image.txt";
```

프로젝트 루트의 ANSI 지원 PowerShell 터미널에서 불꽃 시안 보기:

```powershell
[Console]::Write([IO.File]::ReadAllText((Join-Path $PWD 'Client/resources/type_scripts/10_attack_image.txt'), [Text.Encoding]::UTF8))
```

전체 시안 보기:

```powershell
Get-ChildItem -LiteralPath 'Client/resources/type_scripts' -Filter '*.txt' |
    Sort-Object Name |
    ForEach-Object {
        Write-Host "`n$($_.BaseName)"
        [Console]::Write([IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8))
    }
```

| 파일 | 타입 | 모양 |
| --- | --- | --- |
| `01_attack_image.txt` | 노말 | 충격 |
| `02_attack_image.txt` | 격투 | 주먹 |
| `03_attack_image.txt` | 비행 | 날개 |
| `04_attack_image.txt` | 독 | 해골 |
| `05_attack_image.txt` | 땅 | 갈라진 지면 |
| `06_attack_image.txt` | 바위 | 각진 바위 |
| `07_attack_image.txt` | 벌레 | 딱정벌레 |
| `08_attack_image.txt` | 고스트 | 유령 |
| `09_attack_image.txt` | 강철 | 금속 너트 |
| `10_attack_image.txt` | 불꽃 | 불길 |
| `11_attack_image.txt` | 물 | 물방울 |
| `12_attack_image.txt` | 풀 | 잎사귀 |
| `13_attack_image.txt` | 전기 | 번개 |
| `14_attack_image.txt` | 에스퍼 | 초능력의 눈 |
| `15_attack_image.txt` | 얼음 | 눈송이 |
| `16_attack_image.txt` | 드래곤 | 뿔 달린 용 |
| `17_attack_image.txt` | 악 | 어둠 속 눈 |
| `18_attack_image.txt` | 페어리 | 반짝이는 별 |
