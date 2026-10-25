# TemplateCleanArchNET

Template acadêmico de uma plataforma de compras corporativas construído com .NET 10 e Clean Architecture. O projeto demonstra, com regras de negócio e integrações próximas de um cenário profissional, o fluxo de requisição, orçamento, cotação, aprovação, auditoria e geração de relatórios.

> **Uso exclusivamente acadêmico.** Os dados demonstrativos são fictícios, os provedores externos ficam desabilitados por padrão e esta composição Docker não representa uma topologia de produção.

## O que existe na `main`

- API ASP.NET Core versionada (`v1` e `v2`) com OpenAPI/Swagger.
- autenticação ASP.NET Core Identity, JWT, sessões rotativas e autorização por permissões;
- cadastro de organização, centros de custo, catálogo e fornecedores;
- orçamento, requisições, cotações, propostas, comparações e adjudicação;
- aprovação durável com Azure Functions/Durable Task;
- arquivos em Blob Storage, relatórios PDF/XLSX e processamento assíncrono;
- Outbox/Inbox, RabbitMQ, Redis, Elasticsearch, Kibana, PostgreSQL e observabilidade.

A fase 32 (pedidos de compra e recebimento) está sendo desenvolvida em paralelo e, por isso, não faz parte deste README baseado na `main`.

## Arquitetura em uma visão

```text
Angular/Nginx -> Web API -> Application -> Domain
                    |             ^
                    v             |
          PostgreSQL / Redis / RabbitMQ / Elasticsearch / Azurite
                    |
               Worker + Workflow Functions
```

A direção de dependências em compilação é `Domain <- Application <- Infrastructure`; Web API, Worker e Workflow Functions são composition roots independentes.

## Pré-requisitos

- Docker Desktop recente com Docker Compose v2;
- Git;
- PowerShell 7 ou Windows PowerShell para a criação automática do `.env`;
- aproximadamente 12 GB de memória disponíveis para Docker e 15 GB livres em disco na primeira compilação.

Não é necessário instalar .NET, Node.js, PostgreSQL, Redis ou RabbitMQ para executar a aplicação pelo Docker.

## Execução completa com Docker

### 1. Clone e entre no repositório

```powershell
git clone <URL-DO-REPOSITORIO>
Set-Location TemplateCleanArchNET
```

### 2. Crie o arquivo local de ambiente

O script gera credenciais aleatórias, ativa a massa acadêmica e não imprime segredos no terminal:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-demo-env.ps1
```

O arquivo `.env` é ignorado pelo Git. Para configuração manual, copie `.env.example` para `.env` e substitua **todos** os valores `CHANGE_ME`.

### 3. Compile e inicie tudo

```powershell
docker compose up -d --build
```

Na primeira execução, o Docker baixa as imagens, restaura dependências, compila API/Worker/Functions/Angular, cria os dois bancos, aplica migrations e executa os seeds. Isso pode levar vários minutos.

### 4. Acompanhe a inicialização

```powershell
docker compose ps
docker compose logs -f webapi worker workflow-functions
```

Quando `webapi`, `worker`, `workflow-functions` e as dependências estiverem saudáveis, interrompa apenas a visualização dos logs com `Ctrl+C`.

### 5. Abra a aplicação

- aplicação web: [http://localhost:4200](http://localhost:4200)
- Swagger: [http://localhost:8080/swagger](http://localhost:8080/swagger)
- prontidão da API: [http://localhost:8080/health/ready](http://localhost:8080/health/ready)
- monitor visual dos workflows: [http://localhost:7072](http://localhost:7072)

Todos os serviços são publicados somente em `127.0.0.1`, portanto ficam acessíveis na máquina local sem exposição para a rede.

## Serviços e contêineres

| Contêiner | Serviço | Acesso local |
|---|---|---|
| `templatecleanarch-webapp` | Angular servido por Nginx | `http://localhost:4200` |
| `templatecleanarch-webapi` | ASP.NET Core API e Swagger | `http://localhost:8080` |
| `templatecleanarch-worker` | Outbox, consumidores e relatórios | `http://localhost:8082/health/ready` |
| `templatecleanarch-workflow` | Azure Functions de aprovação | `http://localhost:7071/api/health/live` |
| `templatecleanarch-workflow-monitor` | Durable Functions Monitor | `http://localhost:7072` |
| `templatecleanarch-postgres` | bancos da aplicação e Identity | `localhost:5432` |
| `templatecleanarch-rabbitmq` | broker e Management UI | `amqp://localhost:5672` / `http://localhost:15672` |
| `templatecleanarch-redis` | cache Redis | `localhost:6379` |
| `templatecleanarch-redis-insight` | interface do Redis | `http://localhost:6380` |
| `templatecleanarch-elasticsearch` | busca e logs | `http://localhost:9200` |
| `templatecleanarch-kibana` | interface do Elasticsearch | `http://localhost:5601` |
| `templatecleanarch-files` | Azurite Blob para arquivos/relatórios | `http://localhost:10000` |
| `templatecleanarch-workflow-storage` | Azurite do Durable Task | Blob `11000`, Queue `11001`, Table `11002` |

RabbitMQ usa `RABBITMQ_DEFAULT_USER`/`RABBITMQ_DEFAULT_PASS`; PostgreSQL, Redis e os armazenamentos usam as respectivas credenciais do `.env`. Redis Insight solicita a aceitação da licença na primeira abertura. O Durable Functions Monitor é uma ferramenta administrativa local: ele acessa somente o hub `TemplateApprovals`, permite inspecionar e também controlar instâncias, e não deve ser publicado fora de `127.0.0.1`.

## Massa de dados acadêmica

A massa é ativada por `DEMO_DATA_ENABLED=true`, usa IDs estáveis e pode ser executada mais de uma vez sem duplicar o cenário. Ela contém apenas dados fictícios:

- 10 usuários, um por papel de negócio;
- 3 departamentos e 4 centros de custo;
- 4 categorias, 3 unidades de medida e 6 itens de catálogo;
- 3 fornecedores ativos, com contatos, endereços e categorias;
- período fiscal aberto do ano corrente e 4 alocações em BRL;
- 3 requisições em rascunho com valores e prazos coerentes.

Transações posteriores — submissão, reserva, RFQ, propostas e aprovação — devem ser produzidas pela interface/API. Assim o roteiro exercita validações, concorrência, auditoria, eventos e integrações em vez de inserir estados artificiais diretamente no banco.

### Personas

Todos usam a senha guardada em `DEMO_USER_PASSWORD` no `.env`.

| Usuário | Papel principal | Uso no roteiro |
|---|---|---|
| `demo.admin` | Administrator | organização, catálogo e usuários |
| `demo.requester` | Requester | criação e submissão de requisições |
| `demo.manager` | CostCenterManager | visão da equipe e aprovação de nível 1 |
| `demo.department-head` | DepartmentHead | aprovação de nível 2 |
| `demo.finance` | FinanceApprover | aprovação financeira de nível 3 |
| `demo.budget` | BudgetController | períodos e alocações |
| `demo.buyer` | Buyer | fornecedores, RFQs, propostas e adjudicação |
| `demo.procurement` | ProcurementManager | exceções, políticas e administração do fluxo |
| `demo.receiver` | Receiver | persona reservada ao fluxo de recebimento da fase 32 |
| `demo.auditor` | Auditor | auditoria e relatórios globais |

Os papéis são separados de propósito; não existe um “superusuário” de negócio. Isso demonstra a matriz real de permissões e segregação de funções.

## Roteiro para conhecer o sistema

### 1. Cadastros mestres

1. Entre como `demo.admin`.
2. Visite departamentos, centros de custo, categorias, unidades e itens de catálogo.
3. Observe códigos imutáveis, status ativo/inativo e controle otimista de versão (`ETag`/`If-Match`).
4. No Swagger, consulte `GET /api/v2/organization-settings` e os endpoints de catálogo.

### 2. Orçamento

1. Entre como `demo.budget`.
2. Abra o período fiscal do ano corrente e as quatro alocações.
3. Compare valor total, reservado, comprometido e disponível.
4. Gere depois um relatório de consumo para observar a reconstrução “as of”.

### 3. Requisição e reserva

1. Entre como `demo.requester`.
2. Abra “Minhas requisições”; existem dois rascunhos prontos.
3. Revise itens, quantidade, preço estimado, finalidade e data necessária.
4. Submeta primeiro a requisição de notebooks. A aplicação valida referências e disponibilidade e cria a reserva na mesma transação.
5. Entre como `demo.manager` para conferir a visibilidade por centro de custo.

### 4. Cotação e fornecedores

1. Entre como `demo.buyer`.
2. Consulte os três fornecedores ativos e seus dados comerciais fictícios.
3. Crie a RFQ a partir da requisição submetida, convide fornecedores e publique-a.
4. Registre revisões de proposta, encerre a coleta e gere a comparação.
5. Selecione a proposta vencedora e envie a adjudicação para aprovação.

Use a requisição de operação cloud para testar um valor superior a R$ 100 mil e, consequentemente, todos os níveis de aprovação.

### 5. Aprovação durável

1. Abra o [Durable Functions Monitor](http://localhost:7072) e selecione o hub `TemplateApprovals`.
2. Entre como `demo.manager`, decida o nível 1 e atualize o monitor para observar a instância e sua linha do tempo.
3. Entre como `demo.department-head` e decida o nível 2.
4. Para a requisição acima de R$ 100 mil, entre como `demo.finance` e decida o nível 3.
5. Acompanhe o diagnóstico funcional como `demo.procurement` e compare-o com a visão técnica do monitor.
6. Reinicie `templatecleanarch-workflow`; o histórico e o estado durável devem permanecer no Azurite dedicado.

O painel é diferente do Azure Monitor/Application Insights: no laboratório ele oferece uma visão operacional direta do Durable Task armazenado no Azurite. Logs e métricas gerais da aplicação continuam em console, Elasticsearch e Kibana.

### 6. Relatórios, mensageria e observabilidade

1. Solicite relatórios pela aplicação e acompanhe a execução assíncrona pelo Worker.
2. Abra RabbitMQ Management e observe exchanges, queues e dead-letter queues `procurement.*`.
3. Abra Kibana e consulte o data stream de logs; verifique também `http://localhost:9200/_cluster/health`.
4. Abra Redis Insight e inspecione as chaves de cache depois de consultar produtos.
5. Entre como `demo.auditor` para consultar trilha de auditoria e relatórios permitidos.

## Operação cotidiana

```powershell
# Parar sem apagar dados
docker compose stop

# Iniciar novamente
docker compose start

# Recriar imagens após alteração de código
docker compose up -d --build

# Ver logs de um serviço
docker compose logs -f webapi

# Encerrar e remover contêineres, preservando volumes
docker compose down

# Conferir a configuração final do Compose
docker compose config -q
```

Para reiniciar o laboratório do zero, a remoção de volumes é intencional e destrutiva:

```powershell
docker compose down --volumes --remove-orphans
docker compose up -d --build
```

Isso apaga bancos, filas, índices, caches, arquivos e histórico de workflows locais. O seed será recriado na próxima inicialização.

## Solução de problemas

- **Porta ocupada:** altere somente a porta correspondente no `.env` e execute `docker compose up -d` novamente.
- **API ainda indisponível:** use `docker compose logs webapi` e confirme se PostgreSQL e Elasticsearch estão saudáveis.
- **Workflow indisponível:** confira `workflow-functions` e `workflow-storage`; a chave interna deve ser idêntica nos serviços.
- **Monitor de workflows vazio:** confirme que existe uma aprovação iniciada, selecione `TemplateApprovals` e confira `workflow-monitor` e `workflow-storage` com `docker compose ps`.
- **Relatório não conclui:** confira `worker`, `rabbitmq` e `azurite` e depois `/health/ready` do Worker.
- **Login falha após trocar a senha no `.env`:** recrie o contêiner da API com `docker compose up -d --build --force-recreate webapi`; quando a massa acadêmica está ativa, o seed sincroniza somente as contas `demo.*` com `DEMO_USER_PASSWORD`.
- **Máquina sem memória:** feche outros workloads e aumente a memória do Docker Desktop; Elasticsearch e os builds são os maiores consumidores.

## Desenvolvimento sem Docker

Os SDKs estão fixados no repositório. Para trabalhar localmente, use .NET 10, Node 24.22.0 e pnpm 12.14.0. Os comandos principais são:

```powershell
dotnet restore TemplateCleanArchitecture.sln
dotnet build TemplateCleanArchitecture.App.slnf --no-restore

Set-Location Client/Template.WebApp
pnpm install --frozen-lockfile
pnpm validate
```

Consulte `docs/product/` para regras de negócio e decisões, `docs/phase-2-workspace.md` para arquitetura/coding standards e `docs/phase-3-implementation-plan.md` para o plano de evolução.

## Segurança e limites

- Nunca versione `.env`, tokens, senhas, chaves, certificados ou dumps.
- As portas administrativas são adequadas apenas para loopback/local.
- A massa não contém pessoas, empresas, CNPJs ou contatos reais.
- Provedores de enriquecimento externos permanecem desabilitados por padrão.
- Para qualquer implantação real seriam necessários secret manager, TLS, hardening, backup, políticas de retenção, monitoramento, alta disponibilidade e revisão formal de segurança.

## Licença

Consulte o arquivo [LICENSE](LICENSE). Este repositório é um recurso de estudo e não oferece garantia de adequação a uso produtivo.
