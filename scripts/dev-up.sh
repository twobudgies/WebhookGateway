#!/usr/bin/env bash
# Starts the local dev stack and creates queues, tables, bucket and KMS key (idempotent).
set -euo pipefail
cd "$(dirname "$0")/.."

export AWS_ACCESS_KEY_ID=local AWS_SECRET_ACCESS_KEY=local AWS_DEFAULT_REGION=ap-south-1 AWS_PAGER=""
aws_url="http://localhost:5050"
account="123456789012"
awsl() { aws --endpoint-url "$aws_url" "$@"; }

docker compose -f dev/docker-compose.yml up -d --wait

# Queues follow docs/data-plane.md; each has a DLQ (maxReceiveCount 5).
create_queue() {
  local name="$1" visibility="$2"
  awsl sqs create-queue --queue-name "${name}-dlq" >/dev/null
  local dlq_arn="arn:aws:sqs:${AWS_DEFAULT_REGION}:${account}:${name}-dlq"
  awsl sqs create-queue --queue-name "$name" --attributes \
    "{\"VisibilityTimeout\":\"${visibility}\",\"MessageRetentionPeriod\":\"1209600\",\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"${dlq_arn}\\\",\\\"maxReceiveCount\\\":\\\"5\\\"}\"}" >/dev/null
  echo "queue $name ready"
}
create_queue events 60
create_queue deliveries-basic 30
create_queue deliveries-advanced 30
create_queue deliveries-premium 30

create_table() {
  local name="$1"; shift
  if awsl dynamodb describe-table --table-name "$name" >/dev/null 2>&1; then return; fi
  awsl dynamodb create-table --table-name "$name" --billing-mode PAY_PER_REQUEST "$@" >/dev/null
  echo "created table $name"
}

# docs/data-plane.md section 4
create_table delivery_state \
  --attribute-definitions \
    AttributeName=pk,AttributeType=S \
    AttributeName=gsi1pk,AttributeType=S AttributeName=gsi1sk,AttributeType=S \
    AttributeName=gsi2pk,AttributeType=S AttributeName=gsi2sk,AttributeType=S \
    AttributeName=gsi3pk,AttributeType=S AttributeName=gsi3sk,AttributeType=S \
  --key-schema AttributeName=pk,KeyType=HASH \
  --global-secondary-indexes \
    'IndexName=gsi1,KeySchema=[{AttributeName=gsi1pk,KeyType=HASH},{AttributeName=gsi1sk,KeyType=RANGE}],Projection={ProjectionType=ALL}' \
    'IndexName=gsi2,KeySchema=[{AttributeName=gsi2pk,KeyType=HASH},{AttributeName=gsi2sk,KeyType=RANGE}],Projection={ProjectionType=ALL}' \
    'IndexName=gsi3,KeySchema=[{AttributeName=gsi3pk,KeyType=HASH},{AttributeName=gsi3sk,KeyType=RANGE}],Projection={ProjectionType=ALL}'

create_table rollups \
  --attribute-definitions AttributeName=pk,AttributeType=S AttributeName=sk,AttributeType=S \
  --key-schema AttributeName=pk,KeyType=HASH AttributeName=sk,KeyType=RANGE

if ! awsl s3api head-bucket --bucket webhookgateway-dev >/dev/null 2>&1; then
  awsl s3api create-bucket --bucket webhookgateway-dev \
    --create-bucket-configuration "LocationConstraint=${AWS_DEFAULT_REGION}" >/dev/null
  echo "created bucket webhookgateway-dev"
fi

if [ "$(awsl kms list-aliases --query "length(Aliases[?AliasName=='alias/webhookgateway-dev'])" --output text)" != "1" ]; then
  key_id=$(awsl kms create-key --query KeyMetadata.KeyId --output text)
  awsl kms create-alias --alias-name alias/webhookgateway-dev --target-key-id "$key_id"
  echo "created KMS key alias/webhookgateway-dev"
fi

echo "Local stack is up. See dev/README.md for endpoints."
