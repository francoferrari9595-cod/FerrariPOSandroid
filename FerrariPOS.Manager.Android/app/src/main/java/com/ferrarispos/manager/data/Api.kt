package com.ferrarispos.manager.data

import okhttp3.Interceptor
import java.io.IOException
import okhttp3.OkHttpClient
import retrofit2.Response
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import retrofit2.http.*
import java.util.concurrent.TimeUnit

interface FerrariApi {
    @GET("api/mobile/ping") suspend fun ping(): Response<Map<String,Any>>
    @GET("api/mobile/resumen") suspend fun summary(): Response<Summary>
    @GET("api/mobile/medios-pago") suspend fun paymentMethods(): Response<List<String>>
    @GET("api/mobile/productos") suspend fun products(@Query("q") q:String=""): Response<List<Product>>
    @GET("api/mobile/categorias") suspend fun categories(): Response<List<String>>
    @POST("api/mobile/categorias") suspend fun createCategory(@Body body:CategoryWrite): Response<ApiResult>
    @GET("api/mobile/productos/{id}") suspend fun product(@Path("id") id:Int): Response<Product>
    @GET("api/mobile/producto-comun") suspend fun commonProduct(): Response<CommonProductInfo>
    @POST("api/mobile/productos") suspend fun createProduct(@Body body:ProductWrite): Response<ApiResult>
    @PUT("api/mobile/productos/{id}") suspend fun updateProduct(@Path("id") id:Int,@Body body:ProductWrite): Response<ApiResult>
    @PUT("api/mobile/productos/{id}/proveedor") suspend fun assignSupplier(@Path("id") id:Int,@Body body:SupplierAssignWrite): Response<ApiResult>
    @PUT("api/mobile/productos/{id}/precio") suspend fun updatePrice(@Path("id") id:Int,@Body body:PriceUpdate): Response<ApiResult>
    @POST("api/mobile/productos/{id}/stock") suspend fun stock(@Path("id") id:Int,@Body body:StockUpdate): Response<ApiResult>
    @HTTP(method="DELETE",path="api/mobile/productos/{id}",hasBody=true) suspend fun deleteProduct(@Path("id") id:Int,@Body body:Map<String,String>): Response<ApiResult>
    @GET("api/mobile/clientes") suspend fun customers(): Response<List<Customer>>
    @POST("api/mobile/clientes") suspend fun createCustomer(@Body body:CustomerWrite): Response<ApiResult>
    @PUT("api/mobile/clientes/{id}") suspend fun updateCustomer(@Path("id") id:Int,@Body body:CustomerWrite): Response<ApiResult>
    @HTTP(method="DELETE",path="api/mobile/clientes/{id}",hasBody=true) suspend fun deleteCustomer(@Path("id") id:Int,@Body body:Map<String,String>): Response<ApiResult>
    @POST("api/mobile/clientes/{id}/abono") suspend fun customerPayment(@Path("id") id:Int,@Body body:CustomerPayment): Response<ApiResult>
    @GET("api/mobile/clientes/{id}/cuenta") suspend fun customerAccount(@Path("id") id:Int): Response<CustomerAccountResponse>
    @POST("api/mobile/clientes/{id}/cuenta/limpiar") suspend fun clearCustomerAccountHistory(@Path("id") id:Int): Response<ApiResult>
    @GET("api/mobile/ventas") suspend fun sales(): Response<List<Sale>>
    @POST("api/mobile/ventas") suspend fun createSale(@Body body:SaleWrite): Response<SaleResult>
    @POST("api/mobile/ventas-pendientes") suspend fun sendPendingSale(@Body body:SalePendingWrite): Response<ApiResult>
    @GET("api/mobile/ventas/{id}/ticket") suspend fun ticket(@Path("id") id:Long): Response<TicketResult>
    @GET("api/mobile/tickets-windows") suspend fun windowsTickets(): Response<List<WindowsTicket>>
    @POST("api/mobile/tickets-windows/{id}/agregar") suspend fun appendWindowsTicket(@Path("id") id:String,@Body body:WindowsTicketAppendWrite): Response<ApiResult>
    @POST("api/mobile/tickets-windows/{id}/cobrar") suspend fun chargeWindowsTicket(@Path("id") id:String,@Body body:TableChargeWrite): Response<SaleResult>
    @POST("api/mobile/devoluciones/{saleItemId}") suspend fun returnItem(@Path("saleItemId") saleItemId:Long,@Body body:ReturnWrite): Response<ApiResult>
    @POST("api/mobile/ventas/{id}/cancelar") suspend fun cancelSale(@Path("id") id:Long,@Body body:CancelWrite): Response<ApiResult>
    @GET("api/mobile/caja") suspend fun cash(): Response<CashStatus>
    @GET("api/mobile/usuarios") suspend fun mobileUsers(): Response<List<MobileUser>>
    @GET("api/mobile/usuario/actual") suspend fun currentMobileUser(): Response<MobileUser>
    @POST("api/mobile/usuario/cambiar") suspend fun switchMobileUser(@Body body:MobileUserSwitch): Response<ApiResult>
    @GET("api/arqueo") suspend fun arqueo(): Response<CashArqueo>
    @GET("api/mobile/config") suspend fun config(): Response<MobileConfig>
    @POST("api/mobile/caja/apertura") suspend fun openCash(@Body body:CashOpen): Response<ApiResult>
    @POST("api/mobile/caja/movimiento") suspend fun cashMovement(@Body body:CashMovement): Response<ApiResult>
    @POST("api/mobile/caja/cierre") suspend fun closeCash(@Body body:CashClose): Response<ApiResult>
    @GET("api/mobile/promociones") suspend fun promotions(): Response<List<Promotion>>
    @POST("api/mobile/promociones") suspend fun createPromotion(@Body body:PromotionWrite): Response<ApiResult>
    @PUT("api/mobile/promociones") suspend fun updatePromotion(@Body body:PromotionWrite): Response<ApiResult>
    @HTTP(method="DELETE",path="api/mobile/promociones/{id}",hasBody=true) suspend fun deletePromotion(@Path("id") id:Long,@Body body:Map<String,String>): Response<ApiResult>
    @GET("api/mobile/salones") suspend fun salons(): Response<List<SalonInfo>>
    @GET("api/mobile/mesas") suspend fun tables(@Query("salonId") salonId:Int?=null): Response<List<TableInfo>>
    @GET("api/mobile/tickets-abiertos") suspend fun openTickets(): Response<List<OpenTicket>>
    @POST("api/mobile/tickets-abiertos") suspend fun saveOpenTicket(@Body body:OpenTicket): Response<ApiResult>
    @DELETE("api/mobile/tickets-abiertos/{tableId}") suspend fun deleteOpenTicket(@Path("tableId") tableId:Int): Response<ApiResult>
    @POST("api/mobile/mesas/{tableId}/liberar") suspend fun releaseTable(@Path("tableId") tableId:Int): Response<ApiResult>
    @POST("api/mobile/mesas/{tableId}/cobrar") suspend fun chargeTable(@Path("tableId") tableId:Int,@Body body:TableChargeWrite): Response<SaleResult>
    @POST("api/mobile/tickets-abiertos/{tableId}/agregar") suspend fun appendOpenTicket(@Path("tableId") tableId:Int,@Body body:OpenTicket): Response<ApiResult>
    @GET("api/mobile/analitica") suspend fun analytics(): Response<AnalyticsData>
    @GET("api/mobile/corte-z") suspend fun generalZ(): Response<GeneralZReport>
    @GET("api/mobile/proveedores") suspend fun suppliers(): Response<List<Supplier>>
    @GET("api/mobile/proveedores/{id}/productos") suspend fun supplierProducts(@Path("id") id:Int,@Query("q") q:String=""): Response<List<SupplierProduct>>
    @POST("api/mobile/proveedores") suspend fun createSupplier(@Body body:SupplierWrite): Response<ApiResult>
    @PUT("api/mobile/proveedores/{id}") suspend fun updateSupplier(@Path("id") id:Int,@Body body:SupplierWrite): Response<ApiResult>
    @HTTP(method="DELETE",path="api/mobile/proveedores/{id}",hasBody=true) suspend fun deleteSupplier(@Path("id") id:Int,@Body body:Map<String,String>): Response<ApiResult>
    @GET("api/mobile/compras") suspend fun purchases(): Response<List<Purchase>>
    @POST("api/mobile/compras") suspend fun createPurchase(@Body body:PurchaseWrite): Response<ApiResult>
    @DELETE("api/mobile/compras/{id}") suspend fun deletePurchase(@Path("id") id:Long): Response<ApiResult>
    @GET("api/mobile/compras/{id}") suspend fun purchaseDetail(@Path("id") id:Long): Response<PurchaseDetail>
    @PUT("api/mobile/compras/{id}") suspend fun updatePurchase(@Path("id") id:Long,@Body body:PurchaseWrite): Response<ApiResult>
    @POST("api/mobile/compras/{id}/recibir") suspend fun receivePurchase(@Path("id") id:Long,@Body body:PurchaseReceiveWrite): Response<ApiResult>
    @GET("api/mobile/movimientos-caja") suspend fun cashMovements(): Response<List<Map<String,Any>>>
    @GET("api/mobile/movimientos-stock") suspend fun stockMovements(): Response<List<Map<String,Any>>>
    @POST("api/mobile/movimientos-caja/{id}/anular") suspend fun voidCashMovement(@Path("id") id:Long,@Body body:Map<String,String>): Response<ApiResult>
    @GET("api/mobile/reportes-detallados") suspend fun detailedReports(): Response<DetailedReportData>
    @GET("api/mobile/auditoria") suspend fun auditLog(): Response<List<AuditRow>>
    @GET("api/mobile/vincular/{code}") suspend fun pairingShortCode(@Path("code",encoded=true) code:String): Response<PairingData>
}

object ConnectionRoute {
    @Volatile var activeBaseUrl: String = ""
    @Volatile var fallbackBaseUrl: String = ""

    fun configure(base: String, fallback: String) {
        activeBaseUrl = base.trim().trimEnd('/')
        fallbackBaseUrl = fallback.trim().trimEnd('/')
    }
}

class ApiFactory {
    fun create(baseUrl:String,token:String,fallbackUrl:String=""):FerrariApi {
        ConnectionRoute.configure(baseUrl, fallbackUrl)
        val auth=Interceptor { chain -> chain.proceed(chain.request().newBuilder().addHeader("X-FerrariPOS-Token",token).build()) }
        val route=Interceptor { chain ->
            val original=chain.request()
            val active=ConnectionRoute.activeBaseUrl
            if(active.isBlank()) return@Interceptor chain.proceed(original)
            val parsed=android.net.Uri.parse(active)
            val routed=original.url.newBuilder()
                .scheme(parsed.scheme ?: original.url.scheme)
                .host(parsed.host ?: original.url.host)
                .port(parsed.port.takeIf{it>0} ?: original.url.port)
                .build()
            chain.proceed(original.newBuilder().url(routed).build())
        }
        val retry=Interceptor { chain ->
            val original=chain.request()
            // Nunca repetimos escrituras: una venta, abono, stock o cierre no debe duplicarse.
            if(original.method!="GET") return@Interceptor chain.proceed(original)
            val publicBase=baseUrl.trim().trimEnd('/')
            val fallback=ConnectionRoute.fallbackBaseUrl
            var active=ConnectionRoute.activeBaseUrl.ifBlank { publicBase }
            var last:okhttp3.Response?=null
            var triedFallback=false
            var triedPublic=false
            for(attempt in 0 until 5){
                val target=active.ifBlank { publicBase }
                val parsed=android.net.Uri.parse(target)
                val request=original.newBuilder().url(
                    original.url.newBuilder()
                        .scheme(parsed.scheme ?: original.url.scheme)
                        .host(parsed.host ?: original.url.host)
                        .port(parsed.port.takeIf{it>0} ?: original.url.port)
                        .build()
                ).build()
                try{
                    last?.close()
                    val response=chain.proceed(request)
                    if(response.isSuccessful){
                        ConnectionRoute.activeBaseUrl=target
                        return@Interceptor response
                    }
                    val transient=response.code in setOf(408,429,500,502,503,504,530)
                    if(!transient) return@Interceptor response
                    last=response
                    if(fallback.isNotBlank() && !triedFallback && !target.equals(fallback,ignoreCase=true)){
                        triedFallback=true
                        active=fallback
                        continue
                    }
                    if(!triedPublic && !target.equals(publicBase,ignoreCase=true)){
                        triedPublic=true
                        active=publicBase
                        continue
                    }
                }catch(e:IOException){
                    if(fallback.isNotBlank() && !triedFallback && !active.equals(fallback,ignoreCase=true)){
                        triedFallback=true; active=fallback; continue
                    }
                    if(!triedPublic && !active.equals(publicBase,ignoreCase=true)){
                        triedPublic=true; active=publicBase; continue
                    }
                    if(attempt==4) throw e
                }
                try{Thread.sleep(700L * (attempt+1))}catch(_:InterruptedException){Thread.currentThread().interrupt();break}
            }
            last ?: chain.proceed(original)
        }
        // auth -> route -> retry: también las escrituras usan la ruta activa
        // después de un 530. Las escrituras nunca se duplican; solo cambian de ruta.
        val client=OkHttpClient.Builder().addInterceptor(auth).addInterceptor(route).addInterceptor(retry)
            .connectTimeout(20,TimeUnit.SECONDS).readTimeout(60,TimeUnit.SECONDS).writeTimeout(30,TimeUnit.SECONDS)
            .retryOnConnectionFailure(true).build()
        return Retrofit.Builder().baseUrl(if(baseUrl.endsWith("/"))baseUrl else "$baseUrl/").client(client).addConverterFactory(GsonConverterFactory.create()).build().create(FerrariApi::class.java)
    }
}
