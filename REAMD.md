## About
A Demonstration of a Seamless World MMORPG Server using .NET Orleans

## Installation
### Prerequisite
- [Docker](https://www.docker.com/)
- [minikube](https://minikube.sigs.k8s.io/docs/)
- [helm](https://helm.sh/)

### 1. Run Kubernetes
```sh
$ minikube start
$ minikube dashboard
```

### 2. Deploy helm chart
```sh
$ helm upgrade app ./helm/seamless-world \
    -f ./helm/seamless-world/values.yaml \
    -f ./helm/seamless-world/nats-values.yaml
```

### 3. Expose services
```sh
$ minikube tunnel
```

### 4. Clean resources
```sh
$ helm delete app
```