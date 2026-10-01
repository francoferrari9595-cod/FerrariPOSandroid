package com.ferrarispos.manager.data

import com.google.gson.annotations.SerializedName

data class PairingData(val app:String="",val version:Int=1,val baseUrl:String="",val lanUrl:String="",val token:String="",val store:String="",val mode:String="LAN")
data class Product(val id:Int=0,val barcode:String="",val description:String="",val salePrice:Double=0.0,val wholesalePrice:Double=0.0,val costPrice:Double=0.0,val stock:Double=0.0,val minStock:Double=0.0,val category:String="",val unit:String="UN",val active:Boolean=true,val bulk:Boolean=false,val usesInventory:Boolean=true,val iva21:Boolean=false,val roundTo5:Boolean=false,val supplierId:Int=0,val supplierName:String="")
data class CommonProductInfo(val id:Int=0,val barcode:String="__COMUN__",val description:String="PRODUCTO COMÚN",val active:Boolean=true)
data class MobileConfig(val allowAndroidCharge:Boolean=false)
data class MobileUser(val id:Int=0,val username:String="",val fullName:String="",val role:String="")
data class MobileUserSwitch(val userId:Int,val password:String)
data class Summary(val fecha:String="",val turno:Long=0,val ventas:Double=0.0,val tickets:Long=0,val unidadesStock:Double=0.0,val productos:Long=0,val stockBajo:Long=0,val deudaClientes:Double=0.0,val efectivoTurno:Double=0.0)
data class Sale(val id:Long=0,val ticket:Long=0,val total:Double=0.0,val medio:String="",val canal:String="",val fecha:String="",val cliente:String="",val cajero:String="",val articulos:List<SaleItem> = emptyList())
data class SaleItem(val id:Long=0,val codigo:String="",val producto:String="",val cantidad:Double=0.0,val precioUnitario:Double=0.0,val descuento:Double=0.0,val subtotal:Double=0.0)
data class CashStatus(val id:Long=0,val estado:String="",val apertura:Double=0.0,val aperturaAt:String="",val cierreAt:String="",val cierre:Double?=null,val esperado:Double?=null,val diferencia:Double?=null,val cajero:String="",val mercadoPagoEnabled:Boolean=false,val mercadoPagoOpening:Double=0.0,val mercadoPagoExpected:Double=0.0)
data class CashArqueo(val hayTurno:Boolean=false,val estado:String="",val sesion:Long=0,val cajero:String="",val aperturaAt:String="",val efectivoInicial:Double=0.0,val efectivoVentas:Double=0.0,val efectivoIngresos:Double=0.0,val efectivoEgresos:Double=0.0,val efectivoEsperado:Double=0.0,val mercadoPagoHabilitado:Boolean=false,val mercadoPagoInicial:Double=0.0,val mercadoPagoVentas:Double=0.0,val mercadoPagoIngresos:Double=0.0,val mercadoPagoEgresos:Double=0.0,val mercadoPagoRetencionPorcentaje:Double=0.0,val mercadoPagoRetencion:Double=0.0,val mercadoPagoEsperado:Double=0.0,val totalEsperado:Double=0.0)
data class Customer(val id:Int=0,val name:String="",val document:String="",val phone:String="",val email:String="",val creditLimit:Double=0.0,val deuda:Double=0.0,val abonosHoy:Double=0.0)
data class CustomerDebtDetail(val id:Long=0,val dateTime:String="",val entryType:String="",val amount:Double=0.0,val concept:String="",val paymentMethod:String="",val saleId:Long=0,val ticketNumber:Long=0,val products:String="")
data class CustomerAccountResponse(val customerId:Int=0,val customerName:String="",val balance:Double=0.0,val details:List<CustomerDebtDetail> = emptyList())
data class PriceUpdate(val salePrice:Double)
data class ProductWrite(val barcode:String,val description:String,val salePrice:Double,val wholesalePrice:Double,val costPrice:Double,val stock:Double,val minStock:Double,val category:String,val unit:String,val bulk:Boolean,val usesInventory:Boolean,val iva21:Boolean=false,val stockReductionReason:String="")
data class SupplierAssignWrite(val supplierId:Int,val unitCost:Double=0.0)
data class CategoryWrite(val name:String)
data class StockUpdate(val quantity:Double,val type:String,val reference:String="")
data class CustomerWrite(val name:String,val document:String="",val phone:String="",val email:String="",val address:String="",val creditLimit:Double=0.0)
data class CustomerPayment(val amount:Double,val paymentMethod:String="EFECTIVO",val concept:String="",val reference:String="",val payments:List<PaymentWrite>?=null)
data class SaleLineWrite(
    @SerializedName(value="productId", alternate=["ProductId"]) val productId:Int,
    @SerializedName(value="quantity", alternate=["Quantity"]) val quantity:Double,
    @SerializedName(value="unitPrice", alternate=["UnitPrice"]) val unitPrice:Double,
    @SerializedName(value="discount", alternate=["Discount"]) val discount:Double=0.0,
    @SerializedName(value="description", alternate=["Description"]) val description:String="",
    @SerializedName(value="isCommon", alternate=["IsCommon"]) val isCommon:Boolean=false
)
data class PaymentWrite(val method:String,val amount:Double,val reference:String="")
data class SaleWrite(val customerId:Int=1,val items:List<SaleLineWrite>,val payments:List<PaymentWrite>,val received:Double,val saleChannel:String="SALÓN",val notes:String="",val deliveryAddress:String="",val deliveryStatus:String="N/A",val discountReason:String="")
data class SalePendingWrite(val customerId:Int=1,val customerName:String="",val items:List<SaleLineWrite>,val notes:String="")
data class SaleResult(val ok:Boolean=false,val id:Long=0,val ticket:Long=0,val total:Double=0.0)
data class ReturnWrite(val quantity:Double,val reason:String)
data class CancelWrite(val reason:String)
data class TableChargeWrite(val received:Double,val payments:List<PaymentWrite>)
data class AnalyticsData(val topProducts:List<AnalyticsRow> = emptyList(), val lowStock:List<AnalyticsRow> = emptyList(), val customers:List<AnalyticsRow> = emptyList(), val suppliers:List<AnalyticsRow> = emptyList(), val paymentMix:List<AnalyticsRow> = emptyList())
data class AnalyticsRow(val label:String="", val value:Double=0.0, val extra:Double=0.0)
data class GeneralZReport(val ok:Boolean=false,val date:String="",val title:String="",val text:String="",val tickets:Int=0,val totalSales:Double=0.0,val sessions:Int=0,val cashiers:Int=0,val totalExpectedCash:Double=0.0)
data class CashOpen(val amount:Double,val mercadoPagoEnabled:Boolean=false,val mercadoPagoOpening:Double=0.0,val mercadoPagoRetentionPercent:Double=0.0)
data class CashMovement(val type:String,val concept:String,val amount:Double,val paymentMethod:String="EFECTIVO")
data class CashClose(val counted:Double,val differenceReason:String="",val countedMercadoPago:Double?=null)
data class ApiResult(val ok:Boolean=false,val id:Int=0,val price:Double=0.0)
data class TicketResult(val ok:Boolean=false,val ticket:String="")
data class WindowsTicket(val id:String="",val name:String="",val total:Double=0.0,val items:List<SaleLineWrite> = emptyList(),val tableId:Int=0,val tableName:String="",val customerId:Int=1)
data class WindowsTicketAppendWrite(val items:List<SaleLineWrite>)

data class PromotionItem(val productId:Int=0,val description:String="",val quantity:Double=1.0)
data class Promotion(val id:Long=0,val name:String="",val description:String="",val price:Double=0.0,val active:Boolean=true,val startAt:String="",val endAt:String="",val items:List<PromotionItem> = emptyList())
data class PromotionItemWrite(val productId:Int,val quantity:Double=1.0)
data class PromotionWrite(val id:Long=0,val name:String,val description:String="",val price:Double,val active:Boolean=true,val startAt:String?=null,val endAt:String?=null,val items:List<PromotionItemWrite>)
data class SalonInfo(val id:Int=0,val name:String="",val description:String="",val active:Boolean=true)
data class TableInfo(val id:Int=0,val name:String="",val capacity:Int=4,val shape:String="RECTANGLE",val occupied:Boolean=false,val x:Int=0,val y:Int=0,val width:Int=120,val height:Int=80,val salonId:Int=1,val rotation:Double=0.0,val color:String="")
data class OpenTicket(
    @SerializedName(value="tableId", alternate=["TableId"]) val tableId:Int=0,
    @SerializedName(value="tableName", alternate=["TableName"]) val tableName:String="",
    @SerializedName(value="customerId", alternate=["CustomerId"]) val customerId:Int=1,
    @SerializedName(value="customerName", alternate=["CustomerName"]) val customerName:String="",
    @SerializedName(value="items", alternate=["Items"]) val items:List<SaleLineWrite> = emptyList(),
    @SerializedName(value="notes", alternate=["Notes"]) val notes:String="",
    @SerializedName(value="updatedAt", alternate=["UpdatedAt"]) val updatedAt:String=""
)
data class Supplier(val id:Int=0,val name:String="",val document:String="",val phone:String="",val email:String="",val address:String="")
data class SupplierWrite(val name:String,val document:String="",val phone:String="",val email:String="",val address:String="")
data class SupplierProduct(val id:Int=0,val description:String="",val barcode:String="",val stock:Double=0.0,val costPrice:Double=0.0)
data class PurchaseDetailItem(val productId:Int=0,val description:String="",val quantity:Double=0.0,val unitCost:Double=0.0,val notes:String="",val receivedQuantity:Double=0.0,val remainingQuantity:Double=0.0)
data class PurchaseDetail(val id:Long=0,val orderNo:String="",val supplierId:Int=0,val supplier:String="",val status:String="",val notes:String="",val expectedDate:String="",val items:List<PurchaseDetailItem> = emptyList())
data class PurchaseItemWrite(val productId:Int,val quantity:Double,val unitCost:Double,val notes:String="")
data class PurchaseWrite(val supplierId:Int,val notes:String="",val expectedDate:String="",val items:List<PurchaseItemWrite>)
data class PurchaseReceiveItemWrite(val productId:Int,val quantity:Double,val unitCost:Double,val notes:String="")
data class PurchaseReceiveWrite(val items:List<PurchaseReceiveItemWrite>,val differenceMode:Boolean=false,val closeComplete:Boolean=false,val note:String="")
data class Purchase(val id:Long=0,val orderNo:String="",val supplier:String="",val status:String="",val total:Double=0.0,val receivedTotal:Double=0.0,val date:String="",val expectedDate:String="")
data class ReportRow(val label:String="",val value:Double=0.0,val count:Long=0)
data class DetailedReportData(val sales:Double=0.0,val tickets:Long=0,val averageTicket:Double=0.0,val hourly:List<ReportRow> = emptyList(),val categories:List<ReportRow> = emptyList(),val payments:List<ReportRow> = emptyList())
data class AuditRow(val dateTime:String="",val action:String="",val module:String="",val details:String="",val user:String="")
