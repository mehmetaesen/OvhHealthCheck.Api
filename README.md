# OVH Health Check API

ASP.NET Core 10 API, `/` isteğine `healthy` ve yanıtı veren kopyanın kimliğini döndürür. Aynı kimlik `X-Instance` HTTP başlığında da bulunur. Kubernetes, Pod’un trafiğe hazır olup olmadığını ve çalışmaya devam ettiğini `/healthz` üzerinden kontrol eder.

```text
Yayın  master → GitHub Actions → GHCR + fleet-deploy → Rancher Fleet → RKE2
Trafik hello.voltgamers.com → Caddy → ovh-health Service → A veya B Pod’u
```

## 1. İki kopyayı yerelde çalıştır

Proje klasöründe Docker Desktop’ın Linux motorunu aç ve Compose’u başlat.

```powershell
docker info --format '{{.OSType}}'
docker compose up -d --build
docker compose ps
curl.exe -fsS http://localhost:8080/
```

İlk komut `linux` döndürdüğünde Compose çalıştırılabilir. [localhost:8080](http://localhost:8080/) adresinde `healthy` ve `instance: api-a` veya `instance: api-b` görünür. NGINX iki kopyaya aynı adres üzerinden erişim sağlar.

```powershell
docker compose stop api-a
curl.exe -fsS http://localhost:8080/  # api-b
docker compose start api-a
```

A hazır olduktan sonra B’yi durdurarak testi ters yönde tekrarla. Test sonunda B’yi de başlat.

```powershell
docker compose stop api-b
curl.exe -fsS http://localhost:8080/  # api-a
docker compose start api-b
```

`dockerDesktopLinuxEngine` bağlantısı yoksa Docker Desktop’ın motorunun açılmasını bekle. Ubuntu entegrasyonu uyarısı çıkarsa Windows PowerShell’den yapılan bu test için **Skip WSL distro integration** seçilebilir. Docker motoru açılışta takılırsa açık WSL işlerini kaydet, Docker Desktop’ı kapat, PowerShell’de `wsl --shutdown` çalıştır ve Docker Desktop’ı tekrar aç.

## 2. Projeyi GitHub’a gönder

İlk kurulumda GitHub’da boş bir repo oluştur ve **Code → HTTPS** adresini kopyala. Aşağıdaki ilk kurulum komutları henüz Git deposu bulunmayan bir proje klasörü içindir.

```powershell
git init -b master
git add .
git status --short
git commit -m "Add health check API and Rancher deployment"
git remote add origin https://github.com/mehmetaesen/OvhHealthCheck.Api.git
git push -u origin master
```

Depo zaten bağlıysa `git remote -v` ile adresi kontrol et. `Repository not found` hatasında GitHub’da deponun varlığını ve hesabın yazma yetkisini doğrula. Adres farklıysa `git remote set-url origin <HTTPS-URL>` ile düzelt. `master` korumalıysa değişiklikleri PR ile birleştir.

## 3. GitHub Actions yayınını doğrula

`master` dalına push veya PR merge geldiğinde **Publish to Rancher Fleet** çalışır. Workflow Docker imajını GHCR’a gönderir ve yayınlanan imajın digest’ini içeren Kubernetes manifestlerini `fleet-deploy` dalındaki `k8s` klasörüne yazar. Bu dalı workflow oluşturur ve günceller.

GitHub’da **Actions → Publish to Rancher Fleet** altında en yeni çalışmanın başarılı olduğunu ve `fleet-deploy` dalının oluştuğunu kontrol et. Çalışma sıradaysa GitHub arka planda bekletir. Workflow aynı anda bir dağıtım çalıştırır ve bekleyen çalışmalardan en yenisini tutar.

İlk çalışmada GHCR, imaj katmanlarını aldıktan sonra `denied` döndürdü. Workflow’a `--provenance=false` eklenince tek imaj manifestiyle yapılan yeni çalışma başarılı oldu. Workflow dosyasında değişiklik yaptığında `master`a yeni bir commit gönder ve en yeni Actions çalışmasını aç. **Re-run** seçeneği çalışmayı kendi commit’iyle tekrarlar.

## 4. Rancher Fleet’i dağıtım dalına bağla

Rancher’da **Continuous Delivery → App Bundles** ekranını aç. Bu kurulumda hedef Rancher’ın **Active** durumdaki `local` RKE2 cluster’ı olduğu için sağ üstten `fleet-local` workspace’ini seç. `fleet-default` ekranındaki **No clusters in the workspace** mesajı bu kurulumda workspace değiştirerek çözüldü.

**Create App Bundle → Git Repos** ile ilerle.

| Adım | Alan | Değer |
| --- | --- | --- |
| Metadata | Name | `ovh-health` |
| Repository details | Repository URL | `https://github.com/mehmetaesen/OvhHealthCheck.Api.git` |
| Repository details | Watch ve Branch Name | **A Branch**, `fleet-deploy` |
| Repository details | Custom paths → Root directory | `k8s` |
| Target details | Deploy To | `local`. Workspace’te tek hedef buysa **All Clusters in the workspace** kullanılabilir. |
| Target details | Service Account Name ve Target Namespace | Boş |
| Advanced | Git Authentication | Herkese açık repo için **None** |
| Advanced | TLS Certificate Verification | **Require a valid certificate** |
| Advanced | Enable Polling | Açık |
| Advanced | Enable self-healing | Kapalı |

**Manually specify config files subpaths** kutusunu boş bırak ve **Create** ile kaydet. Fleet polling ile dağıtım dalını düzenli kontrol eder. Self-healing kapalıyken kesinti testi için replica sayısını Rancher’dan geçici olarak değiştirebilirsin. Yeni bir dağıtım Git’teki replica sayısını tekrar uygulayabilir.

## 5. Kubernetes kaynaklarını doğrula

**local → Explore → Workloads → Deployments** altında `ovh-health` namespace’ini seç. `ovh-health-a` ve `ovh-health-b` ayrı ayrı `1/1` olmalı. **Service Discovery → Services** altında `ovh-health` Service’inin `80` portunu ve `app=ovh-health` selector’ını kontrol et. Bu selector iki Deployment’ın hazır Pod’larını kapsar.

Hedef cluster’a bağlı bir kubectl oturumunda aynı kontrolü komut satırından da yapabilirsin.

```bash
kubectl -n ovh-health get deployments,pods,svc,ingress
```

Pod’lar `ImagePullBackOff` durumundaysa **Events** bölümündeki hataya bak. GHCR paketi özelse `ovh-health` namespace’inde paketi okuyabilen bir registry Secret oluştur, Pod’ların kullandığı ServiceAccount’a `imagePullSecrets` olarak ekle ve başarısız Pod’ları yeniden oluştur. Kimlik bilgilerini Rancher veya Kubernetes Secret olarak sakla.

## 6. Caddy’yi yeni Service’e yönlendir

Bu VPS’te alan adının dış HTTPS trafiğini Caddy karşılıyor. Rancher’da Traefik Ingress’i görünse de Caddy’nin `hello.voltgamers.com` bloğu eski `hello-web` Service’inin `10.43.46.198:80` adresine gidiyordu. Bu yüzden yeni Pod’lar hazırken tarayıcıda eski Hello World sayfası görünüyordu.

Rancher’daki **Services** ekranından `ovh-health` Service’inin güncel ClusterIP’sini al. Bu kurulumda `10.43.218.210` idi. Caddy’nin çalıştığı VPS’te önce Service’i doğrudan test et.

```bash
curl -fsS --max-time 5 http://10.43.218.210:80/
```

`healthy` ve `instance: ovh-health-...` görürsen `sudoedit /etc/caddy/Caddyfile` ile yalnızca ilgili HTTPS bloğunun `reverse_proxy` hedefini güncelle.

```caddyfile
hello.voltgamers.com {
    reverse_proxy 10.43.218.210:80
}
```

Yapılandırmayı doğrula, Caddy’yi yeniden yükle ve dış adresten yanıtı kontrol et.

```bash
sudo caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile &&
sudo systemctl reload caddy &&
curl -fsS https://hello.voltgamers.com/
```

Service silinip yeniden oluşturulursa ClusterIP değişebilir. Bu durumda Caddy hedefini Rancher’daki güncel IP ile karşılaştır.

## 7. Canlı A/B kesinti testini yap

[hello.voltgamers.com](https://hello.voltgamers.com/) adresini aç ve `healthy` yanıtını doğrula. Rancher’da **Workloads → Deployments** altında şu sırayla ilerle.

1. `ovh-health-a` kopya sayısını `0` yap ve A Pod’unun durmasını bekle. Aynı URL’yi yenile. `healthy` ve `instance: ovh-health-b-...` görünmeli.
2. A’yı `1`e geri al ve `1/1` olmasını bekle.
3. `ovh-health-b` kopya sayısını `0` yap ve B Pod’unun durmasını bekle. Aynı URL bu kez `healthy` ve `instance: ovh-health-a-...` döndürmeli.
4. B’yi `1`e geri al ve iki Deployment’ın da `1/1` olduğunu kontrol et.

Bu test tek uygulama kopyasının durmasını kapsar. İki Pod aynı node’da çalışıyorsa o node’un kaybı ikisini de etkiler.
