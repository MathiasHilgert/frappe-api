set working-directory := "../.."

# Build the image (Api, Worker and Migrator) as frappe-api:local
image:
    docker build -f deploy/Dockerfile -t frappe-api:local .
