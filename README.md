# OVH Health Check API

ASP.NET Core 10 API, `/` adresinde sağlık durumunu ve yanıt veren kopyayı gösterir. HTTP yanıtında aynı kopya kimliği `X-Instance` başlığında da bulunur. `/healthz` Kubernetes probe adresidir. Sağlık kontrolü API'nin HTTP isteklerine yanıt verebildiğini ölçer. İki kopya tek makinedeyse makine kapanınca ikisi de durur.

## Tek makinede Docker Compose denemesi

Docker Desktop Linux motoru çalışırken:

```powershell
docker compose up -d --build
curl.exe -fsS http://localhost:8080/
```

Tarayıcıda [localhost:8080](http://localhost:8080/) adresini açın. Birkaç kez yenilediğinizde aşağıdaki gibi `api-a` veya `api-b` görürsünüz.

```text
healthy
instance: api-a
```

Aynı URL'yi kullanarak sırayla her kopyayı durdurun. Yeniden başlatılan kopyanın hazır olması ve NGINX'in adresi yeniden çözmesi birkaç saniye sürebilir.

```powershell
docker compose stop api-a
curl.exe -fsS http://localhost:8080/   # api-b
docker compose start api-a
```

```powershell
docker compose stop api-b
curl.exe -fsS http://localhost:8080/   # api-a
docker compose start api-b
```

İki kopyayı durdurunca aynı URL hata döner. Deneme bitince `docker compose down` ile konteynerleri kaldırabilirsiniz.

## GitHub Actions ve Rancher Fleet

`master` dalına push veya PR merge geldiğinde GitHub Actions, API imajını GHCR'a gönderir. Ardından imajın digest'ini içeren manifestleri `fleet-deploy` dalına yazar. Rancher Fleet bu dalı izler ve hedef cluster'a dağıtır.

1. İlk `master` push'undan sonra GitHub **Actions** ekranında **Publish to Rancher Fleet** çalışmasının başarılı olduğunu ve `fleet-deploy` dalının oluştuğunu kontrol edin. Repository politikası workflow'un bu dala yazmasına ve GHCR paketi yayınlamasına izin vermelidir.
2. Rancher **Continuous Delivery → Git Repos** bölümünde bu GitHub deposunu ekleyin. Branch olarak `fleet-deploy`, path olarak `k8s` ve hedef olarak uygulamanın çalışacağı cluster'ı seçin. Özel GitHub deposu için Fleet'e okuma yetkisi verin.
3. GHCR paketi özelse hedef cluster'da `ovh-health` namespace'inde bir registry secret oluşturun ve namespace'in `default` ServiceAccount'una `imagePullSecrets` olarak ekleyin. Registry hesabının paketi okuma yetkisi olmalı. Secret'ı Git'e koymayın. Secret eklendiğinde daha önce oluşturulmuş Pod'ları yeniden oluşturun.
4. Fleet'te GitRepo'nun **Ready** durumunu ve hedef cluster'da **Explore → Workloads** altında `ovh-health-a` ile `ovh-health-b` Deployment'larının hazır olduğunu kontrol edin. Tek makinede denemek için tek node'lu cluster seçin.

### Alan adı ve HTTPS

Cluster'da çalışan bir Ingress controller olmalı. Gerekirse `k8s/ingress.yaml` dosyasında `spec.ingressClassName` alanına controller'ın sınıfını yazın ve değişikliği `master` dalına gönderin. `hello.voltgamers.com` DNS kaydını controller'ın dış IP'sine veya hostname'ine yönlendirin. Alan adına ait geçerli sertifika ve anahtarla TLS Secret oluşturun.

```powershell
kubectl -n ovh-health create secret tls hello-voltgamers-tls --cert=fullchain.pem --key=privkey.pem
kubectl -n ovh-health get ingress
curl.exe -fsS https://hello.voltgamers.com/
```

### Kesinti denemesi

A ve B aynı Service'i paylaşır. Rancher'da veya aşağıdaki komutlarla A'yı 0 kopyaya indirin. [hello.voltgamers.com](https://hello.voltgamers.com/) adresini yenileyince B'nin Pod adı görünmeli. Sonra A'yı geri açın. Yeni bir Fleet dağıtımı, Git'teki kopya sayısını tekrar uygular.

```powershell
kubectl -n ovh-health get pods -w
# Ayrı terminalde:
kubectl -n ovh-health scale deployment/ovh-health-a --replicas=0
curl.exe -fsS https://hello.voltgamers.com/
kubectl -n ovh-health scale deployment/ovh-health-a --replicas=1
```

DNS hazır değilse cluster içinden Service'i test edin.

```powershell
kubectl -n ovh-health run health-client --rm -i --restart=Never --image=busybox:1.37 -- sh -c 'while true; do wget -T 2 -qO- http://ovh-health/ || echo request-failed; sleep 1; done'
```
