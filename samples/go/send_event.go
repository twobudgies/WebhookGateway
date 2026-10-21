// Go 1.20+: `go run send_event.go`. Standard library only. Set WEBHOOKGATEWAY_API_KEY (and WEBHOOKGATEWAY_URL if not local).
package main

import (
	"crypto/rand"
	"fmt"
	"io"
	"net/http"
	"os"
	"strconv"
	"strings"
	"time"
)

func newUUID() string {
	b := make([]byte, 16)
	_, _ = rand.Read(b)
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:])
}

func main() {
	url := os.Getenv("WEBHOOKGATEWAY_URL")
	if url == "" {
		url = "http://localhost:8080"
	}
	apiKey := os.Getenv("WEBHOOKGATEWAY_API_KEY")
	client := &http.Client{Timeout: 10 * time.Second}
	idempotencyKey := newUUID() // one key for all retries of the same event
	body := `{"type":"order.created","data":{"orderId":42,"amount":1999}}`

	for attempt := 0; ; attempt++ {
		req, _ := http.NewRequest("POST", url+"/v1/events", strings.NewReader(body))
		req.Header.Set("Authorization", "Bearer "+apiKey)
		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Idempotency-Key", idempotencyKey)
		resp, err := client.Do(req)
		if err != nil {
			panic(err)
		}
		text, _ := io.ReadAll(resp.Body)
		resp.Body.Close()
		if (resp.StatusCode == 429 || resp.StatusCode == 503) && attempt < 4 {
			wait := 1 << attempt
			if v, err := strconv.Atoi(resp.Header.Get("Retry-After")); err == nil {
				wait = v
			}
			time.Sleep(time.Duration(wait) * time.Second)
			continue
		}
		fmt.Println(resp.StatusCode, string(text))
		return
	}
}
