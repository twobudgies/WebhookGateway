// Go 1.20+: `go run verify_webhook.go`. Verifies a webhook (Standard Webhooks) from environment variables, for the sample test.
package main

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"fmt"
	"math"
	"os"
	"strconv"
	"strings"
	"time"
)

func verify(secret, webhookID, timestamp, signatureHeader, body string, toleranceSeconds float64) bool {
	ts, err := strconv.ParseInt(timestamp, 10, 64)
	// Reject old or future timestamps: stops replays.
	if err != nil || math.Abs(float64(time.Now().Unix()-ts)) > toleranceSeconds {
		return false
	}
	key, err := base64.StdEncoding.DecodeString(strings.TrimPrefix(secret, "whsec_"))
	if err != nil {
		return false
	}
	mac := hmac.New(sha256.New, key)
	mac.Write([]byte(webhookID + "." + timestamp + "." + body))
	expected := mac.Sum(nil)
	// The header can hold several signatures ("v1,abc v1,def") while a secret is being rotated.
	for _, part := range strings.Fields(signatureHeader) {
		if !strings.HasPrefix(part, "v1,") {
			continue
		}
		got, err := base64.StdEncoding.DecodeString(part[3:])
		if err == nil && hmac.Equal(expected, got) {
			return true
		}
	}
	return false
}

func main() {
	tolerance := 300.0
	if v, err := strconv.ParseFloat(os.Getenv("TOLERANCE_SECONDS"), 64); err == nil {
		tolerance = v
	}
	if verify(os.Getenv("SECRET"), os.Getenv("WEBHOOK_ID"), os.Getenv("TIMESTAMP"), os.Getenv("SIGNATURE"), os.Getenv("BODY"), tolerance) {
		fmt.Println("VALID")
	} else {
		fmt.Println("INVALID")
	}
}
