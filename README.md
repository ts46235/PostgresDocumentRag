# PostgresDocumentRag
Ingest and search on documents using Postgres vectorized DB and Azure OpenAI Service

Quickstart

Download/run a vectorized *equiped* postgres docker container
```bash
docker run --rm --name postgres-17 -p 127.0.0.1:5012:5432 -e POSTGRES_PASSWORD=postgres -v postgres_data:/var/lib/postgresql/data -d pgvector/pgvector:pg17
```
Create *rag* database (CreateDatabaseIfNotExists wasn't working)
```bash	
# execute psql in a shell in container
$ docker exec -it postgres-17 ./bin/psql -U postgres
postgres=\# create database rag;
postgres=\# \c rag; #switches to rag db
postgres=\# CREATE EXTENSION IF NOT EXISTS vector;
```
DB connection is set up to hit localhost already

Add your 3 Secrets' value to the secrets file that match appsettings.json

Hit F5 to run

During program execution, it will:
- Create Table if it doesnt exist
- Clear out all the table rows
- Loop thru the resume folder to find each file
- Chunk file into embeddings and store each in a separate row in resume table
- Show a chat interface with which to ask questions to
- Upon each question asked it will:
  - Use an LLM to extract just the actual search portion of the question, minus any instructions
  - Do a similarity vector search against the Postgres DB
  - Inject these results into pre-formed prompt template along with the original question
  - Issue the prompt to LLM
  - Render the response in the chat client