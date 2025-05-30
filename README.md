# PostgresDocumentRag
Ingest and search on documents using Postgres vectorized DB and Azure OpenAI Service

Quickstart

Download/run a vectorized *equiped* postgres docker container
```bash
docker run --rm --name postgres-17 -p 127.0.0.1:5012:5432 -e POSTGRES_PASSWORD=postgres -v postgres_data:/var/lib/postgresql/data -d pgvector/pgvector:pg17
```
Create *rag* database (CreateDatabaseIfNotExists wasn't working)
```bash	
# shell into container's psql
$ docker exec -it postgres-17 ./bin/psql -U postgres
postgres=# create database rag;
postgres=# CREATE EXTENSION IF NOT EXISTS vector;
```
DB connection is set up to hit localhost already

Add your 3 Secrets' value to the secrets file that match appsettings.json

Hit F5 to run
