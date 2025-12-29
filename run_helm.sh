#!/bin/bash

helm install app ./helm/seamless-world \
    -f ./helm/seamless-world/values.yaml \
    -f ./helm/seamless-world/nats-values.yaml
