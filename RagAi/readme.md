Quickstart

Download/run a vectorized postgres docker container
```bash
docker run --rm --name postgres-17 -p 127.0.0.1:5012:5432 -e POSTGRES_PASSWORD=postgres -v postgres_data:/var/lib/postgresql/data -d pgvector/pgvector:pg17`

```
Create a database (CreateDatabaseIfNotExists wasn't working)
```bash	
# shell into container's psql
docker exec -it postgres-17 ./bin/psql -U postgres
create database rag;
```
DB connection is set up to hit localhost already

Secrets are not set up, for now you can search and replace "your azure openAi-endpoint/your apikey" with real values (EmbeddingService.cs)

Hit F5 to run

