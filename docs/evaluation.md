# Offline review evaluation and metrics

CodeGuard AI review kalitesini yalnız compile-time testlerle ölçmez. Offline eval suite, sabit kaynak fixture'larını ve deterministik `FakeLLMProvider` cevaplarını gerçek review parsing/grounding/domain hattından geçirir. Normal test koşusu dış servise, Gemini anahtarına veya ağa bağlanmaz.

## Fixture seti

| Fixture | Beklenen semantik | Kontrol |
|---|---|---|
| `NullDereference.cs` | `Bug` | Gerçek dosya ve geçerli dereference satırı |
| `MissingValidation.cs` | `Reliability` | Gerçek dosya ve validation eksikliği satırı |
| `PathTraversal.cs` | `Security` | Gerçek dosya ve dosya okuma satırı |
| `ResourceDisposal.cs` | `Reliability` | Gerçek dosya ve dispose edilmeyen resource satırı |
| `CleanControl.cs` | Finding yok | False-positive kontrolü |

Assertion'lar title/reason/suggestion cümlelerini karşılaştırmaz. Category, repository-relative file, gerçek context içinde line range, accepted/rejected durumu ve clean-control finding sayısı değerlendirilir. Fake cevap kullanılması model kalitesini puanlamaz; prompt/schema/grounding pipeline'ının beklenen semantiği koruduğuna dair hızlı regresyon sinyali sağlar. Gerçek model benchmark'ı ayrı, opt-in ve secret-aware bir çalışma olmalıdır.

## Çalıştırma

Yalnız offline eval:

```powershell
dotnet test --filter Category=Eval
```

Normal suite eval'leri de içerir:

```powershell
dotnet test
```

## Review log event sözlüğü

| Event ID | Ad | Seviye | Structured alanlar |
|---:|---|---|---|
| 2600 | `Started` | Information | `ReviewRunId`, `ProjectId`, `Model`, `PromptVersion` |
| 2601 | `Completed` | Information | Yukarıdakiler + `DurationMs`, `FindingCount`, `RejectedFindingCount` |
| 2602 | `Failed` | Warning | Kimlik/model alanları + `DurationMs`, `ErrorCode` |

`DurationMs`, orchestration başlangıcından terminal persist tamamlanana kadar geçen wall-clock süredir. `FindingCount` yalnız accepted ve persist edilen finding sayısıdır; `RejectedFindingCount` grounding veya policy nedeniyle reddedilen adayları sayar. Kimlikler correlation sağlar; model ve prompt version regresyonları segmentlemeyi sağlar.

## Redaction sınırı

Allowlist dışında structured alan eklenmez. Özellikle API key/connection string, system veya user prompt metni, repository root/path, kaynak kod, finding reason/suggestion ve provider response loglanmaz. Exception nesnesi provider ya da repository içeriği taşıyabileceği için standart review eventlerine eklenmez. Hata davranışı yalnız bounded `ErrorCode` ile kaydedilir.

`ReviewWorkflowLogTests` hem event ID/alan sözleşmesini hem de sensitive sentinel'ların formatted veya structured loglarda bulunmadığını kanıtlar. Yeni metric eklenirse önce bu sözlük ve allowlist testi birlikte güncellenmelidir.
