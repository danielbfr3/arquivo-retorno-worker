# Regras de negócio

Um worker só: `CnabRetorno.ExcelCnab.Worker`. Ele varre uma pasta,
identifica o cliente dono de cada planilha e entrega a planilha ao
conversor de layout, que faz o CNAB. Compartilha o `CnabRetorno.Core`
(domínio e contratos) e o `CnabRetorno.Common` (infraestrutura HTTP).

| | |
|---|---|
| Projeto | `CnabRetorno.ExcelCnab.Worker` |
| O que faz | Entrega ao conversor as planilhas depositadas numa pasta, identificando o cliente pelo nome do arquivo |
| Gatilho | Varredura periódica (cron) |
| Origem | Pasta local em dev; compartilhamento **SMB** montado no pod em hml/prd |
| Bases | `CASH_COBRANCA` (escreve `Cobranca.Arquivo`) e a base de **adesão** (lê razão social) |
| Converte? | Não faz a conversão — **entrega** ao conversor assíncrono, pipeline `excel-cnab`, appId `cash-cobranca` |
| Storage | Cópia da planilha no **Gestor de Arquivos e no bucket S3, os dois ao mesmo tempo**. Recurso destacável — ver [Como desativar / como remover](#como-desativar--como-remover) |
| Fila | Não consome nenhuma. **Publica** um aviso de conclusão por planilha num tópico SNS — recurso destacável, como o storage |

---

## O fluxo

```mermaid
flowchart TD
    A[Varre a pasta] --> B{Nome casa com<br/>Simplificado_&lt;cnpj&gt;.xlsx/.xls?}
    B -- não --> Q[Move pra Quarentena]
    B -- sim --> C[Extrai o CNPJ do nome]
    C --> D{Cliente na base de adesão,<br/>com razão social?}
    D -- não --> Q
    D -- sim --> E[Gera o ArquivoID GUID]
    E --> F[INSERT em Cobranca.Arquivo<br/>EmProcessamento / EnviadoParaConversao]
    F --> S[Cópias: Gestor de Arquivos + bucket S3<br/>falha vira log de erro, não bloqueia]
    S --> G[POST /v1/convert/async/upload<br/>file + appId + pipeline + id + metadata]
    G -- aceito --> H[Move pra Backup]
    H --> N[Publica aviso no tópico SNS<br/>falha vira log de erro, não reverte nada]
    G -- recusado --> I[UPDATE etapa = ArquivoInvalido] --> Q
```

O corpo da mensagem enviado ao conversor:

```jsonc
// campo "metadata" do multipart (nome configurável — ver Em aberto)
{ "cnpj": "12345678000199", "razaoSocial": "ACME DISTRIBUIDORA LTDA" }
```

E o aviso publicado no tópico SNS, uma mensagem por planilha:

```jsonc
{
  "evento": "planilha-enviada-para-conversao",
  "arquivoId": "11111111-1111-1111-1111-111111111111",
  "arquivoNome": "Simplificado_12345678000199.xlsx",
  "cnpj": "12345678000199",
  "razaoSocial": "ACME DISTRIBUIDORA LTDA",
  "appId": "cash-cobranca",
  "pipeline": "excel-cnab",
  "jobId": "job-123",
  "ocorridoEm": "2026-08-27T12:00:00+00:00"
}
```

"Terminei o processamento" aqui quer dizer: a planilha foi aceita pelo
conversor e saiu da pasta de entrada. **Não** quer dizer que o CNAB está
pronto — quem avisa isso é a mensagem de conclusão do próprio conversor,
que chega depois. O `arquivoId` é o mesmo nos dois eventos, então quem
consome consegue parear.

## Regras por passo

| Passo | Regra | Onde |
|---|---|---|
| Varredura | Só o nível de cima da pasta. Backup e Quarentena vivem dentro dela, e varrer recursivamente reprocessaria o que já foi tratado. | `Origem/PastaOrigemExcel.cs` |
| Varredura | Arquivo modificado há menos de `SegundosEstabilidade` é pulado — ler uma planilha que ainda está sendo copiada pro SMB enviaria meio arquivo ao conversor. | idem |
| Varredura | Arquivos `~$*` são pulados: é a trava que o Excel deixa na pasta enquanto alguém tem a planilha aberta, não a planilha de ninguém. Sem isso, entopem a quarentena todo ciclo. | idem |
| Nome | Máscara configurável, padrão `Simplificado_{cnpj}`, com extensão `.xlsx` ou `.xls`. Casamento **exato**: sufixo, prefixo ou nome parecido não passa. | `Origem/NomeArquivoSimplificado.cs` |
| Nome | O CNPJ pode vir pontuado (`12.345.678.0001-99`) e é normalizado pra 14 dígitos. A barra do CNPJ canônico não entra: é caractere proibido em nome de arquivo. | idem |
| Nome | O CNPJ do nome é a **única** identificação do cliente — a planilha nunca é aberta. Por isso a leitura é estrita: nome fora do padrão vai pra quarentena, nunca vira palpite. Enviar a planilha de um cliente com o CNPJ de outro é pior que não enviar. | idem |
| Cliente | Razão social vem da base de adesão, pelo CNPJ. Cliente ausente (ou sem razão social) **barra** o envio: é parte do payload que o conversor recebe, e uma planilha sem dono identificado não deve virar CNAB. Vai pra quarentena com log de erro. | `Persistencia/EmpresaAdesaoRepository.cs` |
| Conteúdo | A planilha **não é aberta**. O conteúdo é repassado como bytes opacos; quem entende o formato é o pipeline `excel-cnab`. Por isso não há biblioteca de Excel no projeto. | `Pipeline/ProcessadorArquivoExcelService.cs` |
| GUID | O `ArquivoID` nasce antes do envio e vai como `id` da conversão — um id só na cadeia inteira, nunca um GUID novo por chamada. | idem |
| Ordem | A linha em `Cobranca.Arquivo` é criada **antes** da chamada. O conversor é assíncrono e a conclusão chega depois correlacionada pelo `ArquivoID`; sem a linha, a conclusão chega sem ter onde se ancorar (`cash-cobranca-referencia.md` §2.4). | `Persistencia/ArquivoRepository.cs` |
| Registro | Estado inicial `EmProcessamento` / `EnviadoParaConversao` — o par que a máquina de estados da API dona da tabela permite. | idem |
| Cópias | A planilha é gravada nos **dois** destinos habilitados, não em um ou outro. Cada destino liga e desliga sozinho. A chave/id do objeto é o `ArquivoID` — o mesmo da linha no banco, então reprocessar sobrescreve em vez de duplicar. | `Armazenamento/ArmazenadorDeCopias.cs` |
| Cópias | Um destino que falha **não interrompe o outro**: o motivo de existirem dois é não depender de nenhum em particular. | idem |
| Cópias | Falha de cópia sai como **erro** no log e, por padrão, **não bloqueia** o envio ao conversor — guardar cópia é auxiliar ao fluxo principal. `Armazenamento:FalhaBloqueiaEnvio=true` inverte: sem as cópias, o arquivo vai pra quarentena e volta na próxima execução. | idem |
| Cópias | Acontece **antes** do envio: é a partir do envio que o arquivo sai da pasta de entrada, e a cópia é o que sobra dele. | `Pipeline/ProcessadorArquivoExcelService.cs` |
| Gestor de Arquivos | Presign + PUT na URL assinada, em **dois** `HttpClient` distintos. A URL assinada é absoluta e aponta pro S3; reusar o client da API mandaria a credencial dela pra um host de terceiro. | `Armazenamento/GestorArquivoStorage.cs` |
| Aviso SNS | Uma mensagem **por planilha**, só quando o envio foi aceito. Arquivo em quarentena não gera mensagem — fica no log. | `Notificacao/SnsNotificadorConclusao.cs` |
| Aviso SNS | É a **última** coisa do fluxo e **nunca** derruba nada: quando ele acontece, a planilha já foi enviada e o arquivo já saiu da pasta. Deixar o erro subir marcaria como falha um arquivo processado com sucesso — e o próximo ciclo não o reprocessaria, porque ele não está mais na origem. Falha vira erro no log. | `Pipeline/ProcessadorArquivoExcelService.cs` |
| Aviso SNS | Sem retentativa própria. O SDK já traz a dele; além disso, um aviso perdido é reenviável à mão a partir do log, e insistir seguraria a varredura. | idem |
| Content-type | `.xlsx` vai como OOXML e `.xls` como `application/vnd.ms-excel`. São formatos diferentes, não só extensões — mandar tudo como octet-stream obrigaria o pipeline a adivinhar pelo nome. | `Http/LayoutConversaoApiClient.cs` |
| Aceite | Só `status: "pending"` conta como aceite. Um 200 com outro status (ou sem status) é falha, não sucesso silencioso. | idem |
| Falha no envio | A linha vira `ArquivoInvalido` e o arquivo vai pra quarentena. Deixá-lo na origem faria o próximo ciclo criar uma segunda linha, com `ArquivoID` novo, pro mesmo arquivo; deixar a linha como "enviado pra conversão" a deixaria pendurada esperando uma conclusão que nunca chega. | `Pipeline/ProcessadorArquivoExcelService.cs` |
| Falha no envio | Se o `UPDATE` de `ArquivoInvalido` também falhar, o erro é logado como aviso e **não** sobe: o erro que importa é o do envio, e deixar o segundo subir trocaria a causa raiz por um erro de consequência. | idem |
| Movimentação de arquivo | Backup e Quarentena **nunca sobrescrevem**: homônimo ganha sufixo de timestamp. O mesmo cliente manda `Simplificado_<cnpj>` toda semana com o mesmo nome; na quarentena, sobrescrever seria perder justamente a evidência do problema. | `Origem/PastaOrigemExcel.cs` |
| Concorrência | A varredura inteira roda sob `sp_getapplock`. Duas réplicas varrendo a mesma pasta enviariam a mesma planilha duas vezes, cada uma com `ArquivoID` próprio — dois CNABs pro mesmo cliente. | `Persistencia/LockExecucaoExclusiva.cs` |
| Falha isolada | Erro num arquivo não derruba a varredura: vira contador de falha e o resto segue. Só a varredura em si é fatal. | `Pipeline/EnviarPlanilhasPipeline.cs` |

## O que o robô não faz

- **Não lê a planilha.** Nenhuma célula é interpretada aqui.
- **Não gera CNAB.** Quem gera é o pipeline `excel-cnab` do conversor.
- **Não depende do storage pra converter.** A planilha vai no multipart
  da chamada ao conversor; as cópias no Gestor de Arquivos e no bucket são
  um passo à parte, e por padrão uma cópia que falha não impede a
  conversão.
- **Não espera a conversão terminar.** O endpoint é assíncrono: o robô
  termina no aceite, e a mensagem de conclusão é tratada por outro worker
  do ecossistema, que se ancora no `ArquivoID`. O aviso que este worker
  publica no SNS é sobre o **envio**, não sobre o CNAB.
- **Não consome fila nenhuma.** O tópico SNS é só saída.
- **Não deduplica por conteúdo.** Duas planilhas idênticas enviadas em
  ciclos diferentes viram dois envios. O que evita reprocessar a mesma é a
  ida pra Backup no fim do ciclo.

## Em aberto

Dados de integração que ninguém confirmou — todos marcados com
`TODO(a-confirmar)` no código, todos configuráveis:

| O quê | Onde | Se estiver errado |
|---|---|---|
| **Schema da base de adesão** (nome de schema, tabela e colunas) | `Persistencia/AdesaoDbContext.cs` | Nenhuma planilha é enviada — é caminho crítico de todo arquivo |
| **Nome do campo de metadados** no multipart (hoje `metadata`) | `Conversao:CampoMetadados` | O conversor recebe o arquivo sem os dados do cliente |
| **Valores numéricos** de `ArquivoStatus`/`ArquivoEtapa` | `Core/Dominio/Arquivo.cs` | Corrompe o rastreamento de arquivo do ecossistema CASH inteiro |
| **Base URL do conversor** | `LayoutConversaoApi:BaseUrl` | Nenhuma chamada sai |
| **Bucket S3 e base URL do Gestor** | `Armazenamento:S3:Bucket`, `Armazenamento:GestorArquivos:BaseUrl` | Nenhuma cópia é gravada (erro no log, envio segue) |
| **ARN do tópico SNS** | `Notificacao:TopicoArn` | Nenhum aviso é publicado (erro no log, processamento segue) |
| **Autenticação das APIs** (API key? OAuth? mTLS?) | `ApiClientOptions.ApiKey` | Chamada rejeitada |
| **Status de aceite** além de `pending` | `ConvertAsyncUploadResponse.Aceito` | Envio aceito é tratado como falha |

## Como desativar / como remover

O armazenamento de cópias foi construído pra sair do caminho sem deixar
rastro. São três níveis:

Os dois recursos acessórios — as **cópias** (`Armazenamento/`) e o **aviso
no SNS** (`Notificacao/`) — foram construídos com o mesmo desenho, pra
saírem do caminho sem deixar rastro. São três níveis:

**Desligar parcialmente** — `Armazenamento:S3:Habilitado=false` (ou
`GestorArquivos:Habilitado=false`). O outro destino continua gravando.

**Desligar um recurso inteiro** — `Armazenamento:Habilitado=false` ou
`Notificacao:Habilitado=false`. Nada é registrado no DI e o passo vira
no-op; o fluxo principal não muda de forma. Em cluster, isso é uma
variável de ambiente (`Armazenamento__Habilitado=false`,
`Notificacao__Habilitado=false`), sem rebuild.

**Remover o código** — cinco coisas por recurso, e nada além delas:

| | Cópias | Aviso SNS |
|---|---|---|
| 1. Apagar a pasta | `Armazenamento/` | `Notificacao/` |
| 2. No `Program.cs` | a linha `AdicionarArmazenamento(...)` e o `using` | a linha `AdicionarNotificacao(...)` e o `using` |
| 3. No `ProcessadorArquivoExcelService.cs` | o parâmetro `ArmazenadorDeCopias copias` e a chamada `copias.ArmazenarAsync(...)` | o parâmetro `INotificadorConclusao notificador` e o método `NotificarSemDerrubarOEnvioAsync` (mais `IOptions<ConversaoOptions>` e `TimeProvider`, se nada mais os usar) |
| 4. No `appsettings.json` | a seção `Armazenamento` | a seção `Notificacao` |
| 5. No `.csproj` | `PackageReference` do `AWSSDK.S3` | `PackageReference` do `AWSSDK.SimpleNotificationService` |

Mais o arquivo de teste correspondente
(`ArmazenadorDeCopiasTests.cs` / `PlanilhaEnviadaEventoTests.cs`).

Nenhum outro arquivo conhece esses assuntos: `Core` e `Common` não têm
contrato de storage nem de mensageria, e o resto do worker fala só com
`ArmazenadorDeCopias` e `INotificadorConclusao`. Se essa tabela crescer, o
desenho saiu do lugar.

Ver [`riscos-conhecidos.md`](riscos-conhecidos.md) pros riscos de
**comportamento** — onde o código roda sem erro e ainda assim faz a coisa
errada.
