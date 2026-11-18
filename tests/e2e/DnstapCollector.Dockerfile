FROM golang:1.25.6-bookworm AS build
RUN GOBIN=/out go install github.com/dnstap/golang-dnstap/dnstap@ebb538e7d351a58861a8f348491828214a1d8db2

FROM debian:bookworm-slim
COPY --from=build /out/dnstap /usr/local/bin/dnstap
