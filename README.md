## About
A Demonstration of a Seamless World MMORPG Server using .NET Orleans

<a href="https://vimeo.com/1150551992" target="_blank"><img width="723" height="457" alt="스크린샷 2025-12-31 오후 11 47 02" src="https://github.com/user-attachments/assets/0b6570d4-0b02-407c-b575-0d9ead7e29da" /></a>


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

### 2. Install apps with helm chart
```sh
$ helm install app ./helm/seamless-world \
    -f ./helm/seamless-world/values.yaml \
    -f ./helm/seamless-world/nats-values.yaml
```

If you have modified the chart manually, you can update it to reflect the changes.

```sh
helm upgrade app ./helm/seamless-world -f ./helm/seamless-world/values.yaml -f ./helm/seamless-world/nats-values.yaml
```

### 3. Expose services
```sh
$ minikube tunnel
```

### 4. Clean resources
```sh
$ helm delete app
```
