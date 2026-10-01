package com.ferrarispos.licenseadmin

import android.Manifest
import android.app.AlarmManager
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.util.Base64
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.core.app.ActivityCompat
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.ArrowForward
import androidx.compose.material.icons.filled.CalendarMonth
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.Send
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import org.json.JSONArray
import org.json.JSONObject
import java.nio.charset.StandardCharsets
import java.security.KeyFactory
import java.security.PrivateKey
import java.security.Signature
import java.security.spec.PKCS8EncodedKeySpec
import java.security.spec.PSSParameterSpec
import java.security.spec.MGF1ParameterSpec
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.YearMonth
import java.time.ZoneId
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit
import java.util.UUID

private const val PREFIX = "FPOS-LIC-3"
private const val PREFS = "ferrari_license_admin"
private const val LICENSES = "licenses"
private const val CHANNEL_ID = "license_expiration_alerts"
private const val ALERT_DAYS = 5L

private const val PRIVATE_KEY_DER_B64 = "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"

private data class LicenseRecord(
    val id: String, val customer: String, val machine: String, val type: String,
    val issued: String, val expires: String?, val token: String, val active: Boolean
)

private fun privateKey(): PrivateKey {
    val der = Base64.decode(PRIVATE_KEY_DER_B64, Base64.DEFAULT)
    return KeyFactory.getInstance("RSA").generatePrivate(PKCS8EncodedKeySpec(der))
}
private fun b64Url(bytes: ByteArray) = Base64.encodeToString(bytes, Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING)
private fun sign(payload: String): String {
    val signature = Signature.getInstance("SHA256withRSA/PSS")
    signature.setParameter(PSSParameterSpec("SHA-256", "MGF1", MGF1ParameterSpec.SHA256, 32, 1))
    signature.initSign(privateKey()); signature.update(payload.toByteArray(StandardCharsets.UTF_8))
    return PREFIX + "." + b64Url(payload.toByteArray(StandardCharsets.UTF_8)) + "." + b64Url(signature.sign())
}
private fun utc(i: Instant) = DateTimeFormatter.ISO_INSTANT.format(i)
private fun newLicenseId() = "FPOS-" + UUID.randomUUID().toString().replace("-", "").take(16).uppercase()
private fun normalizeMachine(v: String) = v.filterNot(Char::isWhitespace).uppercase()
private fun prefs(c: Context) = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

private fun loadLicenses(c: Context): MutableList<LicenseRecord> {
    val arr = JSONArray(prefs(c).getString(LICENSES, "[]"))
    return MutableList(arr.length()) { i ->
        val o = arr.getJSONObject(i)
        LicenseRecord(o.getString("id"),o.optString("customer"),o.optString("machine"),o.optString("type"),o.getString("issued"),if(o.isNull("expires"))null else o.optString("expires"),o.optString("token"),o.optBoolean("active",true))
    }
}
private fun saveLicenses(c: Context, list: List<LicenseRecord>) {
    val arr = JSONArray(); list.forEach { r -> arr.put(JSONObject().apply { put("id",r.id);put("customer",r.customer);put("machine",r.machine);put("type",r.type);put("issued",r.issued);if(r.expires==null)put("expires",JSONObject.NULL)else put("expires",r.expires);put("token",r.token);put("active",r.active) }) }
    prefs(c).edit().putString(LICENSES,arr.toString()).apply()
}
private fun createChannel(c: Context) {
    if (Build.VERSION.SDK_INT >= 26) (c.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(NotificationChannel(CHANNEL_ID,"Vencimiento de licencias",NotificationManager.IMPORTANCE_HIGH).apply { description="Avisos automáticos 5 días antes del vencimiento" })
}
private fun scheduleAlert(c: Context, r: LicenseRecord) {
    if (!r.active || r.expires == null || r.expires.startsWith("9999-")) return
    runCatching {
        val expiry = Instant.parse(r.expires)
        val whenAt = expiry.minus(ALERT_DAYS, ChronoUnit.DAYS).atZone(ZoneId.systemDefault()).with(LocalTime.of(9,0)).toInstant().toEpochMilli()
        if (whenAt <= System.currentTimeMillis()) return
        val intent = Intent(c, LicenseExpiryReceiver::class.java).apply { putExtra("id",r.id);putExtra("customer",r.customer);putExtra("expires",r.expires) }
        val pi = PendingIntent.getBroadcast(c, r.id.hashCode(), intent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val alarm = c.getSystemService(Context.ALARM_SERVICE) as AlarmManager
        alarm.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, whenAt, pi)
    }
}

class LicenseExpiryReceiver : android.content.BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        createChannel(context)
        val id = intent.getStringExtra("id") ?: ""
        val customer = intent.getStringExtra("customer").orEmpty().ifBlank { "Cliente" }
        val expires = intent.getStringExtra("expires").orEmpty().take(10)
        val n = NotificationCompat.Builder(context, CHANNEL_ID).setSmallIcon(com.ferrarispos.licenseadmin.R.drawable.ferrari_app_icon).setContentTitle("FerrariPOS · licencia por vencer").setContentText("$customer · vence en 5 días ($expires)").setStyle(NotificationCompat.BigTextStyle().bigText("La licencia $id de $customer vence en 5 días. Revisá el calendario del Administrador de Licencias.")).setPriority(NotificationCompat.PRIORITY_HIGH).setAutoCancel(true).build()
        if (Build.VERSION.SDK_INT < 33 || ActivityCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) NotificationManagerCompat.from(context).notify(id.hashCode(),n)
    }
}

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState); createChannel(this)
        if (Build.VERSION.SDK_INT >= 33 && ActivityCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) ActivityCompat.requestPermissions(this, arrayOf(Manifest.permission.POST_NOTIFICATIONS), 7001)
        loadLicenses(this).forEach { scheduleAlert(this,it) }
        setContent { LicenseAdminScreen() }
    }
}

@Composable
private fun LicenseAdminScreen() {
    val context = androidx.compose.ui.platform.LocalContext.current
    var screen by remember { mutableStateOf("dashboard") }
    var licenses by remember { mutableStateOf(loadLicenses(context).sortedBy { it.expires ?: "9999" }) }
    var month by remember { mutableStateOf(YearMonth.now()) }
    var machineId by remember { mutableStateOf("") }; var customerName by remember { mutableStateOf("") }; var licenseId by remember { mutableStateOf("") }
    var action by remember { mutableStateOf("ACTIVATE") }; var type by remember { mutableStateOf("CUSTOM") }; var daysText by remember { mutableStateOf("90") }; var token by remember { mutableStateOf("") }; var status by remember { mutableStateOf("SISTEMA LOCAL · DATOS GUARDADOS") }
    fun refresh() { licenses = loadLicenses(context).sortedBy { it.expires ?: "9999" } }
    fun generate() {
        val machine=normalizeMachine(machineId); val a=action.uppercase(); val t=type.uppercase(); val days=daysText.toIntOrNull()
        if(machine.length<16){status="ERROR · MACHINE ID INCOMPLETO";return}; if((a=="RENEW"||a=="DEACTIVATE")&&licenseId.isBlank()){status="ERROR · INDICÁ EL ID DE LICENCIA";return}; if(a!="DEACTIVATE"&&t!="PERMANENT"&&(days==null||days<1||days>36500)){status="ERROR · DÍAS VÁLIDOS: 1 A 36500";return}
        runCatching {
            val now=Instant.now(); val id=licenseId.ifBlank{newLicenseId()}.uppercase(); val expires=when{a=="DEACTIVATE"->null;t=="PERMANENT"->"9999-12-31T23:59:59.9999999Z";else->utc(now.plus(days!!.toLong(),ChronoUnit.DAYS))}
            val payload=JSONObject().apply{put("Action",a);put("LicenseId",id);put("MachineId",machine);put("Type",t);put("IssuedAtUtc",utc(now));if(expires==null)put("ExpiresAtUtc",JSONObject.NULL)else put("ExpiresAtUtc",expires);put("DurationDays",JSONObject.NULL);put("CustomerName",customerName.trim())}.toString()
            val newToken=sign(payload); val old=loadLicenses(context).toMutableList(); old.removeAll{it.id.equals(id,true)}; val active=a!="DEACTIVATE"; old.add(LicenseRecord(id,customerName.trim(),machine,t,utc(now),expires,newToken,active)); saveLicenses(context,old); scheduleAlert(context,old.last()); token=newToken; status=if(active)"✓ LICENCIA GUARDADA · FIRMA COMPATIBLE WINDOWS · AVISO 5 DÍAS ANTES" else "✓ DESACTIVACIÓN GUARDADA"; refresh()
        }.onFailure { token="";status="ERROR · ${it.message ?: it.javaClass.simpleName}" }
    }
    fun copy() { if(token.isBlank())return; (context.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("FerrariPOS License",token));status="✓ CÓDIGO COPIADO" }
    fun share() { if(token.isBlank())return;context.startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).apply{type="text/plain";putExtra(Intent.EXTRA_TEXT,token)},"Enviar licencia")) }

    val cyan=Color(0xFF35F6FF); val red=Color(0xFFFF4264); val green=Color(0xFF4CFF9A); val bg=Color(0xFF04070B)
    MaterialTheme(colorScheme=darkColorScheme(background=bg,surface=Color(0xFF0B1119),primary=cyan,secondary=red)) {
        Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Color(0xFF020409),Color(0xFF091521),Color(0xFF030509))))) {
            Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp),horizontalAlignment=Alignment.CenterHorizontally) {
                Row(Modifier.fillMaxWidth().padding(horizontal=4.dp),verticalAlignment=Alignment.CenterVertically) {
                    Column(Modifier.weight(1f)){Text("FerrariPOS",color=Color.White,fontSize=29.sp,fontWeight=FontWeight.Black);Text("LICENSE CONTROL CENTER",color=cyan,fontSize=12.sp,fontWeight=FontWeight.Bold,letterSpacing=1.sp);Text("OFFLINE · PERSISTENTE · ALERTAS",color=Color(0xFF8292A4),fontSize=10.sp)}
                    Icon(painter=painterResource(com.ferrarispos.licenseadmin.R.drawable.ferrari_license_logo),contentDescription=null,modifier=Modifier.size(60.dp))
                }
                Spacer(Modifier.height(14.dp))
                Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(7.dp)) {
                    listOf("dashboard" to "INICIO","generate" to "GENERAR","calendar" to "CALENDARIO").forEach { (k,label)->FilterChip(selected=screen==k,onClick={screen=k},label={Text(label,fontSize=10.sp,fontWeight=FontWeight.Bold)},leadingIcon={if(k=="calendar")Icon(Icons.Default.CalendarMonth,null)}) }
                }
                Spacer(Modifier.height(12.dp))
                when(screen){
                    "dashboard" -> {
                        val active=licenses.count{it.active}; val expiring=licenses.count{it.active&&it.expires!=null&&runCatching{ChronoUnit.DAYS.between(LocalDate.now(),Instant.parse(it.expires).atZone(ZoneId.systemDefault()).toLocalDate())}.getOrDefault(9999)<=5};
                        Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(8.dp)){ StatCard("ACTIVAS",active.toString(),green,Modifier.weight(1f));StatCard("VENCEN ≤5 DÍAS",expiring.toString(),red,Modifier.weight(1f));StatCard("TOTAL",licenses.size.toString(),cyan,Modifier.weight(1f)) }
                        Spacer(Modifier.height(12.dp)); NeonCard("PRÓXIMOS VENCIMIENTOS") { licenses.filter{it.active&&it.expires!=null}.take(12).forEach { r-> val d=runCatching{ChronoUnit.DAYS.between(LocalDate.now(),Instant.parse(r.expires).atZone(ZoneId.systemDefault()).toLocalDate())}.getOrDefault(9999); Text("${r.customer.ifBlank{"Sin cliente"}}  ·  ${r.id}",color=Color.White,fontWeight=FontWeight.Bold);Text("${r.expires!!.take(10)}  ·  ${if(d<0)"VENCIDA" else "faltan $d días"}",color=if(d<=5)red else Color(0xFFB5C2D0),fontSize=11.sp);Spacer(Modifier.height(8.dp)) };if(licenses.none{it.active&&it.expires!=null})Text("No hay licencias con vencimiento registrado.",color=Color(0xFF8190A2)) }
                    }
                    "generate" -> NeonCard("NUEVA / RENOVACIÓN / DESACTIVACIÓN") {
                        Text("Todo queda guardado en este teléfono. El calendario y los avisos se actualizan automáticamente.",color=Color(0xFF91A0B1),fontSize=12.sp);Spacer(Modifier.height(10.dp));
                        OutlinedTextField(machineId,{machineId=it.uppercase()},Modifier.fillMaxWidth(),singleLine=true,label={Text("MACHINE ID")});Spacer(Modifier.height(7.dp))
                        Row(horizontalArrangement=Arrangement.spacedBy(7.dp),modifier=Modifier.fillMaxWidth()){OutlinedTextField(customerName,{customerName=it},Modifier.weight(1f),singleLine=true,label={Text("CLIENTE / EMPRESA")});OutlinedTextField(licenseId,{licenseId=it.uppercase()},Modifier.weight(1f),singleLine=true,label={Text("ID LICENCIA")})}
                        Spacer(Modifier.height(7.dp));Text("ACCIÓN",color=Color(0xFF8190A2),fontSize=10.sp);Row(horizontalArrangement=Arrangement.spacedBy(4.dp)){listOf("ACTIVATE","RENEW","DEACTIVATE").forEach{v->FilterChip(action==v,{action=v},label={Text(v,fontSize=9.sp)})}}
                        Spacer(Modifier.height(6.dp));Row(verticalAlignment=Alignment.CenterVertically,horizontalArrangement=Arrangement.spacedBy(7.dp)){Column(Modifier.weight(1f)){Text("TIPO",color=Color(0xFF8190A2),fontSize=10.sp);Row{listOf("CUSTOM","ANNUAL","PERMANENT").forEach{v->FilterChip(type==v,{type=v},label={Text(v,fontSize=9.sp)})}}};OutlinedTextField(daysText,{daysText=it.filter(Char::isDigit).take(5)},Modifier.width(125.dp),singleLine=true,label={Text("DÍAS")},keyboardOptions=KeyboardOptions(keyboardType=KeyboardType.Number),enabled=action!="DEACTIVATE"&&type!="PERMANENT")}
                        Spacer(Modifier.height(10.dp));Button(onClick={generate()},modifier=Modifier.fillMaxWidth().height(54.dp),shape=RoundedCornerShape(15.dp)){Icon(Icons.Default.Add,null);Spacer(Modifier.width(5.dp));Text("GENERAR Y GUARDAR",fontWeight=FontWeight.Black)};Spacer(Modifier.height(8.dp));Text(status,color=if(status.startsWith("✓"))green else if(status.startsWith("ERROR"))red else Color(0xFF9BA8B7),fontSize=11.sp)
                        if(token.isNotBlank()){Spacer(Modifier.height(10.dp));Text(token,color=Color(0xFFDDE6F0),fontSize=8.sp);Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.spacedBy(7.dp)){OutlinedButton({copy()},Modifier.weight(1f)){Icon(Icons.Default.ContentCopy,null);Text("COPIAR")};Button({share()},Modifier.weight(1f)){Icon(Icons.Default.Send,null);Text("ENVIAR")}}}
                    }
                    "calendar" -> {
                        NeonCard("CALENDARIO DE LICENCIAS") {
                            Row(Modifier.fillMaxWidth(),horizontalArrangement=Arrangement.SpaceBetween,verticalAlignment=Alignment.CenterVertically){IconButton({month=month.minusMonths(1)}){Icon(Icons.Default.ArrowBack,null)};Text(month.format(DateTimeFormatter.ofPattern("MMMM yyyy")).uppercase(),color=Color.White,fontWeight=FontWeight.Black,fontSize=16.sp);IconButton({month=month.plusMonths(1)}){Icon(Icons.Default.ArrowForward,null)}}
                            Row(Modifier.fillMaxWidth()){listOf("L","M","X","J","V","S","D").forEach{Text(it,Modifier.weight(1f),color=Color(0xFF6E8196),fontSize=10.sp,textAlign=androidx.compose.ui.text.style.TextAlign.Center,fontWeight=FontWeight.Bold)}}
                            val first=(month.atDay(1).dayOfWeek.value-1); val total=month.lengthOfMonth(); val cells=first+total; Column{for(row in 0 until ((cells+6)/7)){Row(Modifier.fillMaxWidth()){for(col in 0..6){val n=row*7+col-first+1;if(n in 1..total){val date=month.atDay(n);val dayLic=licenses.filter{it.active&&it.expires!=null&&runCatching{Instant.parse(it.expires).atZone(ZoneId.systemDefault()).toLocalDate()==date}.getOrDefault(false)};CalendarDay(n,dayLic.size,Modifier.weight(1f))}else Spacer(Modifier.weight(1f).height(46.dp))}}}}
                            Spacer(Modifier.height(10.dp));Text("Las alertas automáticas se programan para las 09:00, cinco días antes del vencimiento.",color=Color(0xFF8190A2),fontSize=10.sp);Spacer(Modifier.height(8.dp));licenses.filter{it.active&&it.expires!=null}.sortedBy{it.expires}.forEach{r->Text("${r.expires!!.take(10)}  ·  ${r.customer.ifBlank{"Sin cliente"}}",color=if(runCatching{ChronoUnit.DAYS.between(LocalDate.now(),Instant.parse(r.expires).atZone(ZoneId.systemDefault()).toLocalDate())<=5}.getOrDefault(false))red else Color.White,fontSize=11.sp)}
                        }
                    }
                }
                Spacer(Modifier.height(20.dp));Text("FerrariPOS · Administrador de Licencias · datos locales",color=Color(0xFF586879),fontSize=10.sp)
            }
        }
    }
}

@Composable private fun NeonCard(title:String,content:@Composable ColumnScope.()->Unit){Card(Modifier.fillMaxWidth(),shape=RoundedCornerShape(20.dp),colors=CardDefaults.cardColors(containerColor=Color(0xFF09111A)),border=androidx.compose.foundation.BorderStroke(1.dp,Color(0xFF1E5663))){Column(Modifier.padding(16.dp)){Text(title,color=Color(0xFF35F6FF),fontSize=13.sp,fontWeight=FontWeight.Black,letterSpacing=1.sp);Spacer(Modifier.height(10.dp));content()}}}
@Composable private fun StatCard(title:String,value:String,accent:Color,modifier:Modifier){Card(modifier.height(88.dp),shape=RoundedCornerShape(16.dp),colors=CardDefaults.cardColors(containerColor=Color(0xFF0A121B)),border=androidx.compose.foundation.BorderStroke(1.dp,accent.copy(alpha=.45f))){Column(Modifier.padding(11.dp)){Text(title,color=Color(0xFF78899C),fontSize=9.sp,fontWeight=FontWeight.Bold);Text(value,color=accent,fontSize=25.sp,fontWeight=FontWeight.Black)}}}
@Composable private fun CalendarDay(n:Int,count:Int,modifier:Modifier){Box(modifier.height(46.dp).padding(2.dp),contentAlignment=Alignment.Center){Surface(shape=RoundedCornerShape(9.dp),color=if(count>0)Color(0xFF3A1520)else Color(0xFF0D1721),border=if(count>0)androidx.compose.foundation.BorderStroke(1.dp,Color(0xFFFF4264))else null){Column(Modifier.fillMaxSize(),horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.Center){Text(n.toString(),color=Color.White,fontSize=12.sp,fontWeight=FontWeight.Bold);if(count>0)Text("$count",color=Color(0xFFFF4264),fontSize=8.sp)}}}}
