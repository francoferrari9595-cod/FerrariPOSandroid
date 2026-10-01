using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class ReservationsForm:Form
{
    private readonly DateTimePicker date=new(){Format=DateTimePickerFormat.Short}; private readonly DataGridView grid=new(); private readonly TextBox name=new(),phone=new(),hour=new(),notes=new(); private readonly NumericUpDown people=new(){Minimum=1,Maximum=100,Value=2}; private readonly SafeComboBox table=new(); private int id;
    public ReservationsForm(){Text="FerrarisPOS · Reservas";Width=1100;Height=680;StartPosition=FormStartPosition.CenterParent;Build();LoadTables();LoadGrid();}
    private void Build(){Controls.Add(new Label{Text="RESERVAS DEL SALÓN",Location=new Point(20,18),AutoSize=true,Font=new Font("Segoe UI",18,FontStyle.Bold)});date.Location=new Point(20,58);date.Value=DateTime.Today;date.ValueChanged+=(_,_)=>LoadGrid();Controls.Add(date);Field("CLIENTE",100,name);Field("TELÉFONO",145,phone);Field("HORA",190,hour);hour.Text="21:00";Controls.Add(new Label{Text="PERSONAS",Location=new Point(20,240),AutoSize=true});people.Location=new Point(140,235);Controls.Add(people);Controls.Add(new Label{Text="MESA",Location=new Point(20,290),AutoSize=true});table.Location=new Point(140,285);table.Width=300;Controls.Add(table);Controls.Add(new Label{Text="NOTAS",Location=new Point(20,340),AutoSize=true});notes.Location=new Point(140,335);notes.Width=300;notes.Height=70;notes.Multiline=true;Controls.Add(notes);var save=Btn("GUARDAR",20,425,130);save.Click+=(_,_)=>Save();Controls.Add(save);var newb=Btn("NUEVA",160,425,130);newb.Click+=(_,_)=>Clear();Controls.Add(newb);var confirm=Btn("CONFIRMAR",300,425,130);confirm.Click+=(_,_)=>SetStatus("CONFIRMED");Controls.Add(confirm);var cancel=Btn("CANCELAR",440,425,130);cancel.Click+=(_,_)=>SetStatus("CANCELLED");Controls.Add(cancel);grid.Location=new Point(480,55);grid.Size=new Size(580,535);grid.ReadOnly=true;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoGenerateColumns=true;grid.BackgroundColor=Color.White;grid.SelectionChanged+=Selected;Controls.Add(grid);}
    private Button Btn(string t,int x,int y,int w)=>new(){Text=t,Location=new Point(x,y),Width=w,Height=38};
    private void Field(string l,int y,TextBox t){Controls.Add(new Label{Text=l,Location=new Point(20,y+5),AutoSize=true,Font=new Font("Segoe UI",9,FontStyle.Bold)});t.Location=new Point(140,y);t.Width=300;Controls.Add(t);}
    private void LoadTables(){var list=new List<object>{new {Id=0,Name="SIN MESA"}};list.AddRange(TableService.GetTables().Select(x=>new {x.Id,x.Name}));table.DataSource=list;table.DisplayMember="Name";table.ValueMember="Id";}
    private void LoadGrid(){grid.DataSource=ReservationService.List(date.Value).Select(x=>new{ x.Id,x.CustomerName,x.Phone,x.Hour,x.People,x.TableName,x.Status,x.Notes}).ToList();}
    private void Selected(object? s,EventArgs e){if(grid.CurrentRow?.DataBoundItem is null)return;var rid=Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);var r=ReservationService.List(date.Value).FirstOrDefault(x=>x.Id==rid);if(r==null)return;id=r.Id;name.Text=r.CustomerName;phone.Text=r.Phone;hour.Text=r.Hour;people.Value=r.People;notes.Text=r.Notes;table.SelectedValue=r.TableId??0;}
    private void Save()
    {
        try
        {
            var tid = table.SelectedValue is int i && i > 0 ? i : (int?)null;
            var savedId = ReservationService.Save(name.Text, phone.Text, date.Value, hour.Text, (int)people.Value, tid, notes.Text, id);
            LoadGrid();
            var saved = ReservationService.List(date.Value).FirstOrDefault(x => x.Id == savedId);
            if (saved is not null && tid.HasValue && ReservationService.TryGetReservationDateTime(saved, out var reservationTime) && reservationTime >= DateTime.Now && reservationTime <= DateTime.Now.AddHours(2))
                MessageBox.Show($"AVISO: la reserva de la mesa para las {reservationTime:HH:mm} es dentro de las próximas 2 horas.\n\nLa mesa quedará bloqueada para nuevas ventas.", "Reserva próxima", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show("Reserva guardada correctamente.");
            Clear();
        }
        catch(Exception ex) { MessageBox.Show(ex.Message,"Reservas",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
    private void SetStatus(string status){if(id<=0)return;ReservationService.SetStatus(id,status);LoadGrid();}
    private void Clear(){id=0;name.Clear();phone.Clear();hour.Text="21:00";people.Value=2;notes.Clear();if(table.Items.Count>0){var idx=0;for(var i=0;i<table.Items.Count;i++){if(table.Items[i] is not null && table.Items[i].GetType().GetProperty("Id")?.GetValue(table.Items[i]) is int v && v==0){idx=i;break;}}table.SelectedIndex=idx;}else table.SelectedIndex=-1;name.Focus();}
}
